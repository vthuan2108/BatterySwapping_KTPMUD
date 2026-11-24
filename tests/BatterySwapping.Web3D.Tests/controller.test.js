const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
let now=Date.now();
const RealDate=Date;
class TestDate extends RealDate {constructor(...args){super(...(args.length?args:[now]));} static now(){return now;}}
global.Date=TestDate;
const Bms = require('../../src/BatterySwapping.Web3D/js/bmsModel.js');
const sent=[];
function element(){return {dataset:{},style:{},classList:{toggle(){}},replaceChildren(){},setAttribute(){}};}
const elements={};const storage=new Map();
const context={Date:TestDate,URLSearchParams,location:{search:''},localStorage:{getItem(k){return storage.get(k)||null;},setItem(k,v){storage.set(k,v);},removeItem(k){storage.delete(k);}},sessionStorage:{getItem(){return null;},removeItem(){}},document:{getElementById(id){return elements[id]??=element();},createElement:element},MqttSync:{connected(){return true;},publish(...args){sent.push(args);}},BmsModel:Bms,CabinetModel:{setBatteryPresence(){},setBatteryEjected(){},setSlotLedColor(){},updateBatteryDisplay(){},setAlarmLight(){},getStation(){return null;}},ParticleSystem:{isOverheating(){return false;},stopOverheat(){}}};
vm.createContext(context);
let source=fs.readFileSync('src/BatterySwapping.Web3D/js/simulatorController.js','utf8');
source=source.replace('let backend = false;','let backend = true;');
source=source.replace('return { init };','return {init, test:{handleCommand,returnOld,stationData,updateSafety,syncCooldowns,insertOld,takeNew,tick,resetSimulation,snapshotRefresh,render}};');
vm.runInContext(source+'\nglobalThis.controller=SimulatorController;',context);
const {handleCommand,returnOld,stationData,updateSafety,syncCooldowns,insertOld,takeNew,tick,resetSimulation,snapshotRefresh,render}=context.controller.test;
const st=stationData['TRAM-BK01'];st.leased=true;
const slot={id:1,batteryId:'OLD',hasBattery:true,soc:20,soh:85,temperature:65,current:0,voltage:70,isLocked:true,isOverheated:true};
st.slots=[slot];st.snapshot={status:{online:true},sessions:[],batteries:[{maPin:'NEW',soC:91,soH:87,nhietDo:29,dienAp:74}]};
handleCommand({stationId:st.id,commandId:'reset',command:'RESET_ALARM',slotNumber:1,batteryId:'OLD'});
assert.equal(slot.temperature,65,'reset does not invent an ambient temperature');
assert.equal(slot.isLocked,true,'hot reset remains isolated');
assert.equal(slot.isCooling,true,'reset starts cooling');
assert.equal(sent.length,0,'no success ACK before cooling is safe');
let hot=slot.temperature;Bms.step(slot,1);assert(slot.temperature<hot && slot.temperature>35,'cooling takes time');assert.equal(slot.soh,85,'cooling does not restore battery health');
const until=slot.cooldownUntil;
assert.equal(until-now,300000,'overheat creates five minute real lock');
slot.temperature=30;
Bms.step(slot,60,60);updateSafety(st);
assert.equal(slot.cooldownUntil,until,'x60 physics cannot shorten wall clock deadline');
assert.equal(slot.isLocked,true,'cold pin remains locked before five minutes');
assert.equal(st.commands.reset.waitingForCooling,true,'reset success cannot precede five minutes');
handleCommand({stationId:st.id,commandId:'earlyreturn',command:'RETURN_OLD_BATTERY',slotNumber:1,batteryId:'OLD'});
assert(sent.some(x=>x[3]?.commandId==='earlyreturn'&&!x[3].success),'cold pin cannot be removed early');
const reload={...slot,isOverheated:false,isLocked:false,cooldownUntil:null,cooldownStartedAt:null};
st.slots=[reload];syncCooldowns(st,[]);
assert.equal(reload.cooldownUntil,until,'reload restores persisted safety deadline');
assert.equal(reload.isLocked,true,'reload cannot bypass safety lock');
st.slots=[slot];
now=until+1;slot.temperature=40;updateSafety(st);
assert.equal(slot.isLocked,true,'five minutes without safe temperature does not unlock');
assert.equal(st.commands.reset.waitingForCooling,true,'hot pin still awaits cooling after deadline');
slot.temperature=30;updateSafety(st);
assert.equal(st.commands.reset.waitingForCooling,false,'technician reset completes after real deadline and safe temperature');
assert.equal(slot.isLocked,false,'safe confirmed pin unlocks after five minutes');
handleCommand({stationId:st.id,commandId:'replace',command:'REPLACE_BATTERY',slotNumber:1,batteryId:'NEW'});
assert.equal(slot.batteryId,'OLD','replacement waits for physical simulation');
elements['replacement-action'].onclick();
assert.equal(slot.batteryId,'NEW');assert.equal(slot.soc,91);assert.equal(slot.soh,87);assert.equal(slot.temperature,29);
assert(sent.some(x=>x[3]?.commandId==='replace' && x[3].success),'replacement acknowledged after manual action');
handleCommand({stationId:st.id,commandId:'bad',command:'REPLACE_BATTERY',slotNumber:1,batteryId:'UNKNOWN'});
assert(sent.some(x=>x[3]?.commandId==='bad' && !x[3].success),'unknown replacement battery rejected');
slot.temperature=55;Bms.protect(slot);
handleCommand({stationId:st.id,commandId:'hotreturn',command:'RETURN_OLD_BATTERY',slotNumber:1,batteryId:'NEW'});
assert(sent.some(x=>x[3]?.commandId==='hotreturn' && !x[3].success),'hot returned battery cannot be released');
slot.temperature=30;now=slot.cooldownUntil+1;Bms.clear(slot);
handleCommand({stationId:st.id,commandId:'return',command:'RETURN_OLD_BATTERY',slotNumber:1,batteryId:'NEW'});
assert.equal(slot.isDoorOpen,true);assert.equal(slot.batteryId,'NEW','opening does not remove battery');
const fresh={...slot,batteryId:'FRESH',temperature:60,isOverheated:false,cooldownUntil:null,cooldownStartedAt:null};
Bms.step(fresh,0);st.slots=[fresh];
assert.equal(fresh.isCooling,true,'overheat automatically starts cooling without a maintenance command');
const localDeadline=fresh.cooldownUntil;
syncCooldowns(st,[{slotNumber:1,batteryId:'FRESH',startedAt:new TestDate(now).toISOString(),unlockAfter:new TestDate(localDeadline+1000).toISOString(),clearedAt:null}]);
assert.equal(fresh.cooldownUntil,localDeadline+1000,'authoritative later deadline extends local lock');
now=fresh.cooldownUntil+1;fresh.temperature=30;
syncCooldowns(st,[{slotNumber:1,batteryId:'FRESH',startedAt:new TestDate(now-301001).toISOString(),unlockAfter:new TestDate(now-1).toISOString(),clearedAt:new TestDate(now).toISOString()}]);
assert.equal(fresh.isOverheated,false,'backend confirmed clearance removes completed latch');
console.log('PASS device reset cooling, deferred replacement, registered BMS and return safety');

// The first three episodes need a backend command, even after cooling and five minutes.
for(let incident=1;incident<=4;incident++){
  const thermal={id:1,batteryId:'THERMAL',hasBattery:true,soc:30,soh:90,temperature:65,voltage:72,current:0,incidentCount:incident,requiresAdmin:incident===4};
  st.slots=[thermal];st.commands={};Bms.protect(thermal);
  now=thermal.cooldownUntil+1;thermal.temperature=30;updateSafety(st);
  assert.equal(thermal.isLocked,true,'elapsed cooldown alone cannot unlock a device');
  if(incident<4){
    handleCommand({stationId:st.id,commandId:'auto'+incident,command:'AUTO_RESET_ALARM',slotNumber:1,batteryId:'THERMAL',preserveStationLock:incident===2});
    updateSafety(st);
    assert.equal(thermal.isOverheated,false,'backend auto reset clears first three thermal episodes');
    assert.equal(thermal.isLocked,incident===2,'automatic recovery preserves a station emergency lock');
  } else {
    handleCommand({stationId:st.id,commandId:'auto4',command:'AUTO_RESET_ALARM',slotNumber:1,batteryId:'THERMAL'});
    updateSafety(st);assert.equal(thermal.isLocked,true,'fourth episode rejects automatic clearance');
    handleCommand({stationId:st.id,commandId:'admin4',command:'RESET_ALARM',slotNumber:1,batteryId:'THERMAL'});
    updateSafety(st);assert.equal(thermal.isLocked,false,'fourth episode accepts safe administrator clearance');
  }
}
const early={id:1,batteryId:'EARLY',hasBattery:true,soc:30,soh:90,temperature:65,voltage:72,current:0};
st.slots=[early];st.commands={};Bms.protect(early);
handleCommand({stationId:st.id,commandId:'earlyauto',command:'AUTO_RESET_ALARM',slotNumber:1,batteryId:'EARLY'});
early.temperature=30;updateSafety(st);assert.equal(early.isLocked,true,'auto command cannot skip five real minutes');
now=early.cooldownUntil+1;early.temperature=40;updateSafety(st);assert.equal(early.isLocked,true,'auto command cannot skip safe temperature');
early.temperature=30;updateSafety(st);assert.equal(early.isLocked,false);

const lateCutoff={id:1,batteryId:'LATE',hasBattery:true,soc:30,soh:90,temperature:65,voltage:72,current:0};
st.slots=[lateCutoff];st.commands={};Bms.protect(lateCutoff);
handleCommand({stationId:st.id,commandId:'queued-auto',command:'AUTO_RESET_ALARM',slotNumber:1,batteryId:'LATE',preserveStationLock:false});
handleCommand({stationId:st.id,commandId:'late-cutoff',command:'CUT_OFF'});
now=lateCutoff.cooldownUntil+1;lateCutoff.temperature=30;updateSafety(st);
assert.equal(lateCutoff.isOverheated,false,'safe reset clears the thermal latch');
assert.equal(lateCutoff.isLocked,true,'a newer emergency cutoff survives an older reset');
tick(1);assert.equal(lateCutoff.current,0,'emergency cutoff keeps charging off');
st.cutOffLatched=false;

(async()=>{
  let appliedLease=null,resetCalls=0;
  context.crypto={randomUUID:()=> 'same-reset-request'};
  context.MqttSync.clientId=()=> 'client';context.MqttSync.leaseId=()=> 'old-lease';
  context.MqttSync.applyLease=(id,lease)=>{appliedLease=lease;};
  const snapshot={status:{online:true},operatingState:'HoatDong',sessions:[],commands:[],cooldowns:[],batteries:[{maPin:'RANDOM',dungLuongThietKe:45}],slots:[{soThuTu:1,maPinHienTai:'RANDOM',soC:47,soH:92,nhietDo:29,dienAp:73.816,dongDien:15,cuaDangMo:false,khoaBaoTri:false}]};
  let requestId=null;
  context.MqttSync.json=async(path,options)=>{
    if(path.endsWith('/simulation/reset')){
      const body=JSON.parse(options.body);requestId ||= body.requestId;
      assert.equal(body.requestId,requestId,'uncertain retry uses identical request');
      assert.equal(options.headers.Authorization,'Bearer admin-token');
      assert.equal(st.resetting,true,'reset pauses device before network request');
      if(++resetCalls===1)throw Error('response lost');
      return {resetId:requestId,stationId:st.id,leaseId:'new-lease'};
    }
    return snapshot;
  };
  st.commands={old:{}};st.pending=[{event:'OLD_INSERTED',sessionId:'old',slotNumber:1}];
  await assert.rejects(resetSimulation('admin-token',st.id));
  assert.equal(st.resetting,true,'uncertain reset keeps old telemetry paused');
  const count=sent.length;tick(90);assert.equal(sent.length,count,'paused reset cannot send stale telemetry');
  await resetSimulation('admin-token',st.id);
  assert.equal(appliedLease,'new-lease');assert.equal(st.resetting,false);
  assert.equal(st.slots[0].soc,47);assert.equal(st.slots[0].designCapacityAh,45);
  assert.equal(st.pending.length,0);assert.equal(Object.keys(st.commands).length,0);
  const first=st.slots[0].soc;await snapshotRefresh(st.id);assert.equal(st.slots[0].soc,first,'refresh preserves server randomized reset data');
  const before=st.slots[0].soc;tick(17.5);assert(Math.abs(st.slots[0].soc-before-15*17.5/3600/(45*.92)*100)<1e-9,'controller advances by actual elapsed seconds');
  context.MqttSync.json=async()=>{const error=Error('maintenance active');error.status=409;throw error;};
  await assert.rejects(resetSimulation('admin-token',st.id));
  assert.equal(st.resetting,false,'definitive first-request rejection resumes the existing station');
  assert.equal(storage.has('bss-reset:'+st.id),false,'definitively rejected reset clears its request');
  const saved={requestId:'saved-request',clientId:'saved-client',leaseId:'saved-old-lease'};
  storage.set('bss-reset:'+st.id,JSON.stringify(saved));
  st.leased=false;st.resetting=true;st.slots=[];
  context.controller.test.render();assert.equal(elements['reset-simulation'].disabled,false,'reload can retry a pending reset without acquiring a new lease');
  context.MqttSync.json=async(path,options)=>{
    if(path.endsWith('/simulation/reset')){
      assert.deepEqual(JSON.parse(options.body),saved,'reload replays the original identity, request and lease');
      return {resetId:'saved-request',stationId:st.id,leaseId:'saved-result-lease'};
    }
    return snapshot;
  };
  await resetSimulation('admin-token',st.id);
  assert.equal(st.leased,true,'successful replay activates the returned lease');
  assert.equal(appliedLease,'saved-result-lease');assert.equal(st.resetting,false);
  console.log('PASS first three automatic recoveries, fourth admin lock, elapsed ticks and idempotent reset');
})().catch(error=>{console.error(error);process.exitCode=1;}).finally(()=>{global.Date=RealDate;});
