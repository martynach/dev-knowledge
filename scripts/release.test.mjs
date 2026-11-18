import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
const sha = value => createHash('sha256').update(value).digest('hex');
test('release verification rejects altered assets and feature provenance for PROD',()=>{
  const dir=mkdtempSync(join(tmpdir(),'dk-release-'));
  const run=target=>spawnSync(process.execPath,[resolve('scripts/verify-release.mjs'),dir,target],{encoding:'utf8'});
  try {
    mkdirSync(join(dir,'frontend'));writeFileSync(join(dir,'api.tar'),'isolated-container-fixture');writeFileSync(join(dir,'frontend','index.html'),'validated frontend');
    const manifest={commit:'a'.repeat(40),sourceRef:'refs/heads/main',buildId:'123',apiSha256:sha('isolated-container-fixture'),frontend:{'index.html':sha('validated frontend')}};
    writeFileSync(join(dir,'manifest.json'),JSON.stringify(manifest));
    writeFileSync(join(dir,'promotion.json'),JSON.stringify({commit:manifest.commit,buildId:'123',image:'registry.test/api@sha256:'+'b'.repeat(64)}));
    assert.equal(run('dev').status,0);assert.equal(run('prod').status,0);
    writeFileSync(join(dir,'frontend','index.html'),'tampered');assert.notEqual(run('prod').status,0);
    writeFileSync(join(dir,'frontend','index.html'),'validated frontend');manifest.sourceRef='refs/heads/feature';writeFileSync(join(dir,'manifest.json'),JSON.stringify(manifest));assert.notEqual(run('prod').status,0);assert.equal(run('dev').status,0);
  } finally {rmSync(dir,{recursive:true,force:true});}
});
test('runtime config disables mock auth and includes restrictive security headers',()=>{
  const dir=mkdtempSync(join(tmpdir(),'dk-config-'));
  try {
    const result=spawnSync(process.execPath,[resolve('scripts/configure-frontend.mjs'),dir,'a'.repeat(40)],{env:{...process.env,API_ORIGIN:'https://api.example.test',OIDC_ISSUER:'https://identity.example.test/v2.0',OIDC_CLIENT_ID:'client',OIDC_SCOPE:'openid profile api://test/Articles.Access'},encoding:'utf8'});
    assert.equal(result.status,0,result.stderr);const config=JSON.parse(readFileSync(join(dir,'config.json')));assert.equal(config.developmentAuth,false);
    const hosting=JSON.parse(readFileSync(join(dir,'staticwebapp.config.json')));assert.match(hosting.globalHeaders['Content-Security-Policy'],/object-src 'none'/);assert.equal(hosting.navigationFallback.rewrite,'/index.html');
  } finally {rmSync(dir,{recursive:true,force:true});}
});
