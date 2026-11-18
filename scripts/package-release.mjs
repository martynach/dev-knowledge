import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';
const dir = process.argv[2];
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
function files(dir) { return readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?files(join(dir,e.name)):[join(dir,e.name)]); }
const commit = execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim();
const sourceRef = process.env.RELEASE_SOURCE_REF || process.env.BUILD_SOURCEBRANCH || 'local';
writeFileSync(join(dir,'manifest.json'),JSON.stringify({commit,sourceRef,buildId:process.env.BUILD_BUILDID,apiSha256:hash(join(dir,'api.tar')),frontend:Object.fromEntries(files(join(dir,'frontend')).map(p=>[relative(join(dir,'frontend'),p).replaceAll('\\','/'),hash(p)]))},null,2));
