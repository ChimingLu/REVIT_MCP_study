import { RevitSocketClient } from '../../build/socket.js';
import fs from 'fs';
import path from 'path';

const payloadPath = process.argv[2]
  ? path.resolve(process.argv[2])
  : path.resolve('../../scripts/ramp/apply_ramp_superelevation_payload.json');

async function main() {
  const client = new RevitSocketClient('localhost', 8964);

  if (!fs.existsSync(payloadPath)) {
    console.error(`[ERROR] payload not found: ${payloadPath}`);
    process.exit(1);
  }

  const payload = JSON.parse(fs.readFileSync(payloadPath, 'utf-8'));
  if (!payload.floorId) {
    console.error('[ERROR] payload.floorId is required');
    process.exit(1);
  }

  console.log('[INFO] Connecting to Revit MCP socket...');
  await client.connect();
  console.log('[INFO] Connected');

  console.log('[INFO] Calling apply_ramp_superelevation...');
  const response = await client.sendCommand('apply_ramp_superelevation', payload, 120000);

  if (!response.success) {
    console.error('[ERROR] apply_ramp_superelevation failed');
    console.error(response.error || 'Unknown error');
    process.exit(1);
  }

  const data = response.data || {};
  console.log('[OK] apply_ramp_superelevation done');
  console.log(`[OK] AppliedCount: ${data.AppliedCount ?? 'n/a'}`);

  const targetElevations = data.TargetElevations || {};
  const keys = Object.keys(targetElevations).slice(0, 10);
  if (keys.length > 0) {
    console.log('[INFO] Sample target elevations (first 10 keys):');
    for (const k of keys) {
      console.log(`  ${k}: ${targetElevations[k]}`);
    }
  }
}

main().catch((err) => {
  console.error('[ERROR] Script crashed:', err?.message || err);
  process.exit(1);
});
