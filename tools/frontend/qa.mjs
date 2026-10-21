/** Focused final QA. All API traffic is synthetic; never reads a live player. */
import assert from 'node:assert/strict';
import {mkdir,writeFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import {fixture,CHARACTER_ID} from './fixtures.mjs';
const {chromium}=await import(pathToFileURL(process.env.PLAYWRIGHT_MODULE).href);
const base='http://127.0.0.1:55052';const output='.artifacts/qa';await mkdir(output,{recursive:true});
const browser=await chromium.launch();const results=[];
try {
 for(const width of [360,390]) {
  const data=fixture();const errors=[],writes=[];
  data.sources.push('Table Homebrew');
  for(const [id,source,auroraId] of [['spell-legacy',data.sources[0],'ID_PHB_SPELL_TEST_CIRCLE'],['spell-custom','Table Homebrew','ID_MY_TABLE_SPELL_CIRCLE']]) {
   data.options.push({...data.options[2],elementId:id,source});
   data.entries.push({...data.entries.find(e=>e.id==='spell-2'),id,source,auroraId,description:`<p>Distinct ${id} mechanics.</p>`});
  }
  data.choices[0].selected={elementId:'spell-reprint',name:'Circle of Shelter',source:data.sources[2]};
  data.choices.push({...data.choices[1],ruleId:'choice-cleric',registrationId:'cleric-casting',parentName:'Cleric Spellcasting'});
  const context=await browser.newContext({viewport:{width,height:900},reducedMotion:'reduce'});
  await context.addInitScript(()=>localStorage.setItem('starlights.player','Test Player'));
  await context.route(u=>u.pathname.startsWith('/api/'),async route=>{
   const req=route.request();try{
    if(req.method()!=='GET'&&!req.url().includes('/session'))writes.push(req.url());
    await route.fulfill({json:data.resolve(req.url(),req.method(),req.postDataJSON())});
   }catch(e){errors.push(e.message);await route.fulfill({status:501,json:{}})}
  });
  const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(15000);
  const shot=async name=>{await page.evaluate(async()=>{scrollTo(0,0);await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame)});await page.screenshot({path:`${output}/${name}-${width}.png`,fullPage:true});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),name+' overflow');};
  const pickers=page.locator('section.spell-browser');
  const available=i=>pickers.nth(i).locator('div.spell-browser');
  const card=(i,source)=>available(i).locator('article').filter({has:page.getByText(source,{exact:true})});
  await page.goto(`${base}/characters/${CHARACTER_ID}`);await page.getByRole('tab',{name:/^Magic/}).click();
  await pickers.first().getByRole('button',{name:'Replace spell',exact:true}).click();
  await available(0).getByRole('textbox',{name:'Search available spells'}).fill('Circle');
  await available(0).getByText('3 spells',{exact:true}).waitFor();
  assert.equal(await available(0).getByRole('heading',{name:'Circle of Shelter',exact:true}).count(),3,'2014, 2024 and homebrew cards');
  assert.ok(await card(0,data.sources[2]).getByRole('button',{name:'Learned',exact:true}).isDisabled());
  await card(0,data.sources[2]).locator('summary').click();
  await card(0,data.sources[2]).getByRole('combobox',{name:/Book printing/}).selectOption('spell-2');
  assert.equal(data.choices[0].selected.elementId,'spell-reprint','Preview preserves saved printing');assert.equal(writes.length,0,'Browsing has no writes');
  await shot('spell-identity');
  await pickers.first().getByRole('button',{name:'Close list',exact:true}).click();
  await pickers.nth(1).getByRole('button',{name:'Choose spell',exact:true}).click();
  await available(1).getByRole('textbox',{name:'Search available spells'}).fill('Circle');
  await available(1).getByText('3 spells',{exact:true}).waitFor();
  assert.ok(await card(1,data.sources[1]).getByRole('button',{name:'Learn',exact:true}).isDisabled(),'Identical reprint already learned');
  assert.ok(await card(1,data.sources[0]).getByRole('button',{name:'Learn',exact:true}).isEnabled(),'Legacy version independent');
  assert.ok(await card(1,'Table Homebrew').getByRole('button',{name:'Learn',exact:true}).isEnabled(),'Homebrew independent');
  await pickers.nth(1).getByRole('button',{name:'Close list',exact:true}).click();
  await pickers.nth(2).getByRole('button',{name:'Choose spell',exact:true}).click();
  await available(2).getByRole('textbox',{name:'Search available spells'}).fill('Circle');
  await available(2).getByText('3 spells',{exact:true}).waitFor();
  assert.ok(await card(2,data.sources[1]).getByRole('button',{name:'Learn',exact:true}).isEnabled(),'Other class retains server-approved choice');
  await pickers.nth(2).getByRole('button',{name:'Close list',exact:true}).click();
  await pickers.first().getByRole('button',{name:'Replace spell',exact:true}).click();
  await available(0).getByRole('textbox',{name:'Search available spells'}).fill('Circle');
  await card(0,data.sources[0]).getByRole('button',{name:'Learn',exact:true}).click();
  await pickers.first().getByRole('button',{name:'Replace spell',exact:true}).waitFor();
  assert.equal(data.choices[0].selected.elementId,'spell-legacy','Explicit replacement retains requested ID');
  await page.getByRole('button',{name:'Save',exact:true}).click();await page.getByText('No unsaved changes.',{exact:true}).waitFor();await page.reload();
  assert.equal(data.choices[0].selected.elementId,'spell-legacy','Saved ID survives reload');
  await page.getByRole('tab',{name:/^Magic/}).click();await pickers.first().getByRole('button',{name:'Remove',exact:true}).click();
  await pickers.first().getByRole('button',{name:'Choose spell',exact:true}).waitFor();assert.equal(data.choices[0].selected,null,'Explicit removal');
  await page.getByRole('button',{name:'Save',exact:true}).click();await page.getByText('No unsaved changes.',{exact:true}).waitFor();
  await page.goto(`${base}/characters/${CHARACTER_ID}/play`);await page.getByRole('heading',{name:/^Mira Vale$/i}).waitFor();
  await page.getByRole('textbox',{name:'Amount',exact:true}).fill('5');await page.getByRole('button',{name:'Temp HP',exact:true}).click();
  await page.waitForFunction(()=>!document.querySelector('fieldset')?.disabled);assert.equal(data.combat.temporaryHitPoints,5);
  await page.getByRole('textbox',{name:'Amount',exact:true}).fill('2');await page.getByRole('button',{name:'Damage',exact:true}).click();await page.waitForFunction(()=>!document.querySelector('fieldset')?.disabled);assert.equal(data.combat.temporaryHitPoints,3);assert.equal(data.combat.damage,3);
  await page.getByRole('link',{name:'Spellcasting',exact:true}).click();await page.getByRole('button',{name:'Unprepare',exact:true}).click();
  await page.locator('#spellcasting').getByRole('button',{name:'Prepare',exact:true}).first().click();
  await page.getByRole('button',{name:'Unprepare',exact:true}).waitFor();assert.equal(await page.locator('#spellcasting > details').getAttribute('open'),'');
  await page.getByRole('button',{name:'Long rest',exact:true}).click();await page.getByRole('button',{name:'Take the long rest',exact:true}).click();
  await page.waitForFunction(()=>!document.querySelector('fieldset')?.disabled);assert.equal(data.combat.damage,0);assert.equal(data.combat.temporaryHitPoints,0);assert.deepEqual(data.magic.expendedSlots,{});
  await shot('sheet-play');
  for(const theme of ['Porcelain','Slate','Midnight']){
   await page.getByRole('button',{name:'Choose theme and units',exact:true}).click();await page.getByRole('button',{name:new RegExp('^'+theme+' ')}).click();await page.getByRole('button',{name:'Save theme',exact:true}).click();
   await page.goto(base);const logo=page.getByRole('img',{name:'Project Starlights',exact:true});await logo.waitFor();await logo.evaluate(async img=>{await img.decode();await new Promise(requestAnimationFrame)});assert.ok(await logo.evaluate(img=>img.complete&&img.naturalWidth>0));await shot('logo-'+theme.toLowerCase());
  }
  assert.deepEqual(errors,[]);results.push({width,spellIdentity:true,previewNoWrites:true,multiclass:true,replacementSaveReload:true,explicitRemoval:true,playState:true,themes:true});
  await context.close();console.log('PASS focused QA',width);
 }
 await writeFile(`${output}/browser-results.json`,JSON.stringify(results,null,2));
}finally{await browser.close();}
