import assert from 'node:assert/strict';
import {pathToFileURL} from 'node:url';
import {fixture,CHARACTER_ID} from './fixtures.mjs';
const {chromium}=await import(pathToFileURL(process.env.PLAYWRIGHT_MODULE).href);
const browser=await chromium.launch();const context=await browser.newContext({viewport:{width:390,height:900},reducedMotion:'reduce'});
const data=fixture();let failSave=false;const base=process.env.FRONTEND_URL??'http://127.0.0.1:55052';
await context.route(u=>u.pathname.startsWith('/api/'),async r=>{
 const q=r.request();if(failSave&&q.method()==='PUT'&&q.url().endsWith('/combat'))return r.fulfill({status:503,json:{message:'Synthetic save failure'}});
 await r.fulfill({json:data.resolve(q.url(),q.method(),q.postDataJSON())});
});
const page=await context.newPage();
try{
 await page.goto(`${base}/characters/${CHARACTER_ID}/play`);await page.getByRole('heading',{name:/^Mira Vale$/i}).waitFor();
 const nav=page.getByRole('link',{name:'Spellcasting',exact:true});await nav.focus();await page.keyboard.press('Enter');
 assert.equal(await page.locator('#spellcasting > details').getAttribute('open'),'','Keyboard navigation opens collapsed section');
 await page.getByRole('button',{name:'Choose theme and units',exact:true}).focus();await page.keyboard.press('Enter');
 await page.getByRole('dialog').waitFor();await page.getByRole('button',{name:/^Midnight Deep navy/}).focus();await page.keyboard.press('Enter');
 assert.equal(await page.locator('html').getAttribute('data-theme'),'midnight');await page.keyboard.press('Escape');
 assert.equal(await page.locator('html').getAttribute('data-theme'),'porcelain','Escape cancels preview');
 await page.waitForFunction(()=>document.activeElement?.getAttribute('aria-label')==='Choose theme and units');
 failSave=true;const before=data.combat.damage;
 await page.getByRole('textbox',{name:'Amount',exact:true}).fill('2');await page.getByRole('button',{name:'Heal',exact:true}).click();
 await page.getByText('A change could not be saved. Check your connection and try again.',{exact:true}).waitFor();assert.equal(data.combat.damage,before,'Failed save did not persist');
 await page.screenshot({path:'.artifacts/screenshots/sheet-save-failure-390.png',fullPage:true});
 failSave=false;await page.getByRole('textbox',{name:'Amount',exact:true}).fill('1');await page.getByRole('button',{name:'Heal',exact:true}).click();await page.getByText('Play state is shared with the battle simulator. Changes save automatically.',{exact:true}).waitFor();assert.equal(data.combat.damage,before-1,'Retry saved');
 console.log('PASS keyboard section navigation, theme keyboard preview/Escape/focus, save failure rollback and retry');
}finally{await context.unrouteAll({behavior:'wait'});await browser.close();}
