const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const local=new Map([['bss-reset:TRAM-BK01',JSON.stringify({requestId:'request',clientId:'client',leaseId:'original-lease'})]]);
const session=new Map([['bss-device-client','client']]);
const storage=map=>({getItem:k=>map.get(k)||null,setItem:(k,v)=>map.set(k,v)});
const requests=[],published=[];
const context={Date,AbortSignal,crypto:{randomUUID:()=> 'new-message'},localStorage:storage(local),sessionStorage:storage(session),console,
  fetch:async(url,options)=>{requests.push(url);return {ok:true,json:async()=>url.endsWith('config')?{brokerUrl:'ws://test'}:{leaseId:'cg-lease'}};},
  mqtt:{connect:()=>({connected:true,on(){},publish:(topic,body)=>published.push(JSON.parse(body))})}};
vm.createContext(context);
vm.runInContext(fs.readFileSync('src/BatterySwapping.Web3D/js/mqttSync.js','utf8')+'\nglobalThis.sync=MqttSync;',context);
(async()=>{
  await context.sync.connect(['TRAM-BK01','TRAM-CG02'],()=>{},()=>{});
  assert.equal(context.sync.clientId(),'client','reload keeps the reset request client identity');
  assert.equal(requests.filter(path=>path.endsWith('device/lease')).length,1,'pending reset does not acquire a replacement lease');
  assert.equal(context.sync.leaseId('TRAM-BK01'),'original-lease');
  context.sync.applyLease('TRAM-BK01','reset-lease');
  context.sync.publish('TRAM-BK01','telemetry',[]);
  assert.equal(published[0].leaseId,'reset-lease','all subsequent telemetry uses rotated reset lease');
  console.log('PASS persisted MQTT client, pending reset lease recovery and adopted reset lease');
})().catch(error=>{console.error(error);process.exitCode=1;});
