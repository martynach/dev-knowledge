import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
const dir=process.argv[2];const manifest=JSON.parse(readFileSync(join(dir,'manifest.json'),'utf8'));
writeFileSync(join(dir,'promotion.json'),JSON.stringify({commit:manifest.commit,buildId:manifest.buildId,image:process.env.IMAGE_DIGEST},null,2));
