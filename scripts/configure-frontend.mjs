import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
const [dir,commit] = process.argv.slice(2);
const {API_ORIGIN,OIDC_ISSUER,OIDC_CLIENT_ID,OIDC_SCOPE} = process.env;
for(const url of [API_ORIGIN,OIDC_ISSUER]) if(!url?.startsWith('https://')) throw Error('HTTPS API and issuer required');
if(!OIDC_CLIENT_ID || !OIDC_SCOPE) throw Error('OIDC config required');
writeFileSync(join(dir,'config.json'),JSON.stringify({apiUrl:API_ORIGIN,developmentAuth:false,issuer:OIDC_ISSUER,clientId:OIDC_CLIENT_ID,scope:OIDC_SCOPE,commit,sourceRef:process.env.RELEASE_SOURCE_REF||'refs/heads/main'}));
writeFileSync(join(dir,'release.json'),JSON.stringify({commit}));
writeFileSync(join(dir,'staticwebapp.config.json'),JSON.stringify({navigationFallback:{rewrite:'/index.html',exclude:['/config.json','/release.json','/*.{js,css,ico,png,svg}']},globalHeaders:{'X-Content-Type-Options':'nosniff','Referrer-Policy':'strict-origin-when-cross-origin','Content-Security-Policy':`default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self' ${new URL(API_ORIGIN).origin} ${new URL(OIDC_ISSUER).origin}; img-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'`},routes:[{route:'/config.json',headers:{'Cache-Control':'no-store'}},{route:'/release.json',headers:{'Cache-Control':'no-store'}}]}));
