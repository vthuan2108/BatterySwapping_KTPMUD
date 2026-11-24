/* Educational 72 V model; design capacity comes from the registered battery. */
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
  function hardFault(slot) { return !!slot && (!!slot.requiresAdmin || ((slot.incidentCount||0)>=3 && locked(slot))); }
  function ready(slot, now=Date.now()) { return slot.temperature<35 && now>=(slot.cooldownUntil||0); }
  function clear(slot) {slot.cooldownUntil=null;slot.cooldownStartedAt=null;slot.isOverheated=false;slot.isCooling=false;slot.isLocked=false;slot.requiresAdmin=false;}
  function step(slot, seconds, scale=1) {
    protect(slot);
    if (!slot.hasBattery) { slot.current=0;slot.voltage=0;slot.isCharging=false;return; }
    if(slot.temperature>=55) {slot.isOverheated=true;slot.isLocked=true;slot.isEjected=false;slot.isDoorOpen=false;}
    // Integrate at current-stage boundaries: background delays never discard charge time.
    let remaining=Number.isFinite(seconds)&&Number.isFinite(scale)?Math.max(0,seconds)*Math.max(0,scale):0;
    const capacity=(slot.designCapacityAh>0?slot.designCapacityAh:30)*Math.max(0.5,slot.soh/100);
    do {
      slot.current=slot.isLocked||slot.isOverheated||slot.soc>=100||slot.isDoorOpen ? 0 : slot.soc<80?15:slot.soc<98?8:2;
      const boundary=slot.soc<80?80:slot.soc<98?98:100;
      const toBoundary=slot.current>0?(boundary-slot.soc)*capacity*36/slot.current:Infinity;
      const dt=Math.min(remaining,toBoundary);
      slot.soc=Math.min(100,Math.max(0,slot.soc+slot.current*dt/3600/capacity*100));
      const heat=slot.current*slot.current*0.08;
      const cooling=slot.isCooling?250:2.5;
      const equilibrium=28+heat/cooling;
      slot.temperature=equilibrium+(slot.temperature-equilibrium)*Math.exp(-cooling*dt/9000);
      remaining=Math.max(0,remaining-dt);
      if(dt===toBoundary)slot.soc=boundary;
      if(slot.temperature>=55)protect(slot);
    } while(remaining>0);
    slot.current=slot.isLocked||slot.isOverheated||slot.soc>=100||slot.isDoorOpen ? 0 : slot.soc<80?15:slot.soc<98?8:2;
    slot.isCharging=slot.current>0;
    slot.voltage=67.2+12.8*(slot.soc/100)+slot.current*0.04;
  }
  return {step,protect,locked,hardFault,ready,clear,minimumLockMs};
})();
if(typeof module!=="undefined") module.exports=BmsModel;
