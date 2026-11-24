/* Assumed 72 V / 30 Ah pack; educational model, not calibrated to a physical BMS. */
const BmsModel = (() => {
  const minimumLockMs = 5 * 60 * 1000;
  function protect(slot, now=Date.now()) {
    if(slot.temperature>=55 && !slot.cooldownUntil) {
      slot.cooldownStartedAt=now;slot.cooldownUntil=now+minimumLockMs;
    }
    if(slot.cooldownUntil || slot.isOverheated || slot.temperature>=55) {
      slot.isOverheated=true;slot.isCooling=true;slot.isLocked=true;
      slot.current=0;slot.isCharging=false;slot.isEjected=false;slot.isDoorOpen=false;
    }
  }
  function locked(slot, now=Date.now()) { return !!slot && (slot.isOverheated || slot.temperature>=55 || now<(slot.cooldownUntil||0)); }
  function ready(slot, now=Date.now()) { return slot.temperature<35 && now>=(slot.cooldownUntil||0); }
  function clear(slot) {slot.cooldownUntil=null;slot.cooldownStartedAt=null;slot.isOverheated=false;slot.isCooling=false;slot.isLocked=false;}
  function step(slot, seconds, scale=1) {
    protect(slot);
    if (!slot.hasBattery) { slot.current=0;slot.voltage=0;slot.isCharging=false;return; }
    if(slot.temperature>=55) {slot.isOverheated=true;slot.isLocked=true;slot.isEjected=false;slot.isDoorOpen=false;}
    const dt=Math.max(0,Math.min(seconds,60))*scale;
    const capacity=30*Math.max(0.5,slot.soh/100);
    slot.current=slot.isLocked||slot.isOverheated||slot.soc>=100||slot.isDoorOpen ? 0 : slot.soc<80?15:slot.soc<98?8:2;
    slot.isCharging=slot.current>0;
    slot.soc=Math.min(100,Math.max(0,slot.soc+slot.current*dt/3600/capacity*100));
    if(slot.soc>=100){slot.current=0;slot.isCharging=false;}
    // 28 C ambient, I²R heating, lumped thermal mass and passive/fan cooling.
    const heat=slot.current*slot.current*0.08;
    const cooling=slot.isCooling?250:2.5;
    const equilibrium=28+heat/cooling;
    slot.temperature=equilibrium+(slot.temperature-equilibrium)*Math.exp(-cooling*dt/9000);
    slot.voltage=67.2+12.8*(slot.soc/100)+slot.current*0.04;
    if(slot.temperature>=55){slot.current=0;slot.isCharging=false;slot.isLocked=true;slot.isOverheated=true;}
  }
  return {step,protect,locked,ready,clear,minimumLockMs};
})();
if(typeof module!=="undefined") module.exports=BmsModel;
