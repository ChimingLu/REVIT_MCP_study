import { RevitSocketClient } from '../../build/socket.js';

const floorId = 16066502;

// 依既有基準高程，先鎖回尾端區域（避免終點邊緣折落到下層）。
const tailRestore = {
  5: 3107.5,
  62: 2808.0,
  63: 2907.0,
  64: 2927.0,
  65: 3013.2,
  66: 3049.5,
  67: 3204.8
};

async function main() {
  const client = new RevitSocketClient('localhost', 8964);
  console.log('[INFO] Connecting to Revit...');
  await client.connect();

  const offsets = Object.entries(tailRestore).map(([index, offsetMm]) => ({
    index: Number(index),
    offsetMm: Number(offsetMm)
  }));

  console.log('[INFO] Restoring tail vertices:', offsets.map(x => x.index).join(','));
  const response = await client.sendCommand('set_floor_vertex_offsets', {
    floorId,
    offsets
  }, 60000);

  if (!response.success) {
    console.error('[ERROR] restore failed:', response.error || 'Unknown error');
    process.exit(1);
  }

  console.log('[OK] restore response:', JSON.stringify(response.data || response, null, 2));
}

main().catch((err) => {
  console.error('[ERROR] Script crashed:', err?.message || err);
  process.exit(1);
});
