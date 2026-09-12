import { RevitSocketClient } from '../../build/socket.js';

async function main() {
  const client = new RevitSocketClient('localhost', 8964);
  console.log('[INFO] connecting');
  await client.connect();
  console.log('[INFO] connected');

  const response = await client.sendCommand('get_selected_elements', {}, 30000);
  console.log(JSON.stringify(response, null, 2));
}

main().catch((err) => {
  console.error('[ERROR]', err?.message || err);
  process.exit(1);
});
