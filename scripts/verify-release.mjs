import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
const [dir,target] = process.argv.slice(2);
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
const manifest = JSON.parse(readFileSync(join(dir,'manifest.json'),'utf8'));
if(!/^[a-f0-9]{40}$/.test(manifest.commit)) throw Error('Invalid commit');
if(hash(join(dir,'api.tar')) !== manifest.apiSha256) throw Error('Container artifact checksum mismatch');
for(const [file,checksum] of Object.entries(manifest.frontend)) {
  if(file.includes('..') || file.startsWith('/')) throw Error('Unsafe artifact path');
  if(hash(join(dir,'frontend',file)) !== checksum) throw Error('Frontend checksum mismatch: '+file);
}
if(target === 'prod') {
  const promotion = JSON.parse(readFileSync(join(dir,'promotion.json'),'utf8'));
  if(manifest.sourceRef !== 'refs/heads/main' || promotion.commit !== manifest.commit || promotion.buildId !== manifest.buildId || !/^.+@sha256:[a-f0-9]{64}$/.test(promotion.image)) throw Error('PROD requires a matching validated main artifact promoted through DEV');
}
