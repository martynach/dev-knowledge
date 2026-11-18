import { readFileSync, readdirSync } from 'node:fs';
import { parse } from '../frontend/node_modules/yaml/dist/index.js';
const root=new URL('../',import.meta.url);
for(const file of readdirSync(new URL('pipelines/',root)).filter(f=>f.endsWith('.yml'))) {parse(readFileSync(new URL('pipelines/'+file,root),'utf8'));console.log('Parsed pipelines/'+file);}
parse(readFileSync(new URL('docker-compose.yml',root),'utf8'));console.log('Parsed docker-compose.yml');
