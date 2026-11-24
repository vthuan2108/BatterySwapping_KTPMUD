const SimulatorController = (() => {
  const STATION_IDS = ["TRAM-BK01", "TRAM-CG02"];
  const $ = (id) => document.getElementById(id);

  let view;
  let activeStationId = new URLSearchParams(location.search).get("station") || "TRAM-BK01";
  if (!STATION_IDS.includes(activeStationId)) activeStationId = "TRAM-BK01";

  const stationData = {
    "TRAM-BK01": {
      id: "TRAM-BK01",
      name: "Trạm 1: ĐH Bách Khoa Hà Nội",
      shortName: "TRAM-BK01 • BÁCH KHOA",
      address: "Cổng Parabol Giải Phóng, P. Bách Khoa, Hai Bà Trưng, Hà Nội",
      posX: -1.55,
      accentColor: "#38bdf8",
      slots: [],
      selectedSlot: 1,
      snapshot: null,
      commands: JSON.parse(localStorage.getItem("bss-commands:TRAM-BK01") || "{}"),
      pending: JSON.parse(localStorage.getItem("bss-events:TRAM-BK01") || "[]"),
      leased: false,
    },
    "TRAM-CG02": {
      id: "TRAM-CG02",
      name: "Trạm 2: Cầu Giấy",
      shortName: "TRAM-CG02 • CẦU GIẤY",
      address: "Số 144 Xuân Thủy, Q. Cầu Giấy, Hà Nội",
      posX: 1.55,
      accentColor: "#10b981",
      slots: [],
      selectedSlot: 1,
      snapshot: null,
      commands: JSON.parse(localStorage.getItem("bss-commands:TRAM-CG02") || "{}"),
      pending: JSON.parse(localStorage.getItem("bss-events:TRAM-CG02") || "[]"),
      leased: false,
    },
  };
  for(const id of STATION_IDS)stationData[id].resetting=!!localStorage.getItem("bss-reset:"+id);

  let backend = false;
  let token = sessionStorage.getItem("bss-token");
  let user = null;
  let session = null;
  let simSeconds = 0;
  let scale = 1;
  let updating = false;
  let currentFee = null;

  function notice(text) {
    const el = $("notice");
    if (el) el.textContent = text;
  }

  async function api(path, method = "GET", body, authToken = token) {
    return MqttSync.json(path, {
      method,
      headers: authToken ? { Authorization: "Bearer " + authToken } : {},
      ...(body ? { body: JSON.stringify(body) } : {}),
    });
  }

  async function resetSimulation(adminToken, stationId=activeStationId) {
    const st=stationData[stationId];
    const storageKey="bss-reset:"+stationId;
    let request=JSON.parse(localStorage.getItem(storageKey)||"null");
    if(!st || st.resetInFlight || (!st.leased && !request) || !MqttSync.connected())return;
    const wasPending=!!request;
    request ||= {requestId:crypto.randomUUID(),clientId:MqttSync.clientId(),leaseId:MqttSync.leaseId(stationId)};
    localStorage.setItem(storageKey,JSON.stringify(request));
    st.resetting=true;st.resetInFlight=true;st.resetEpoch=(st.resetEpoch||0)+1;
    try {
      const result=await api(`stations/${stationId}/simulation/reset`,"POST",request,adminToken);
      MqttSync.applyLease(stationId,result.leaseId);
      st.leased=true;
      st.commands={};st.pending=[];st.slots=[];st.snapshot=null;
      localStorage.setItem("bss-cooldowns:"+stationId,"[]");
      persist(stationId);
      localStorage.removeItem(storageKey);
      st.resetting=false;
      await snapshotRefresh(stationId);
      notice(`Đã reset ${stationId}; thông số pin mới được lưu tại máy chủ.`);
    } catch(error) {
      // A lost response may already have rotated the lease. Keep the request and pause
      // this station until the same request is retried, without sending stale telemetry.
      if(!wasPending && error.status && error.status<500){localStorage.removeItem(storageKey);st.resetting=false;}
      notice(error.message+(st.resetting?" Trạm tạm dừng; bấm Reset Simulation để thử lại cùng yêu cầu.":""));
      throw error;
    } finally {st.resetInFlight=false;render();}
  }

  function persist(stId) {
    const ids = stId ? [stId] : STATION_IDS;
    for (const id of ids) {
      localStorage.setItem("bss-commands:" + id, JSON.stringify(stationData[id].commands));
      localStorage.setItem("bss-events:" + id, JSON.stringify(stationData[id].pending));
      localStorage.setItem("bss-cooldowns:" + id, JSON.stringify(stationData[id].slots.filter(s=>s.cooldownUntil).map(s=>({slotNumber:s.id,batteryId:s.batteryId,startedAt:s.cooldownStartedAt,unlockAfter:s.cooldownUntil,incidentCount:s.incidentCount,requiresAdmin:s.requiresAdmin}))));
    }
  }

  function syncCooldowns(st, rows=[]) {
    let saved=[];try{saved=JSON.parse(localStorage.getItem("bss-cooldowns:"+st.id)||"[]");}catch{}
    for(const slot of st.slots){
      const local=saved.find(r=>r.slotNumber===slot.id);
      if(local && (!local.batteryId || local.batteryId===slot.batteryId)){
        slot.incidentCount=local.incidentCount||slot.incidentCount||0;slot.requiresAdmin=!!local.requiresAdmin;
        slot.cooldownStartedAt=slot.cooldownStartedAt||local.startedAt;
        slot.cooldownUntil=Math.max(slot.cooldownUntil||0,local.unlockAfter||0);
      }
      const server=rows.find(r=>r.slotNumber===slot.id);
      if(server){
        slot.incidentCount=server.incidentCount||0;slot.requiresAdmin=!!server.requiresAdmin;
        const start=Date.parse(server.startedAt),until=Date.parse(server.unlockAfter),cleared=server.clearedAt?Date.parse(server.clearedAt):null;
        if(cleared && (slot.cooldownUntil || slot.isOverheated) && cleared>=(slot.cooldownStartedAt||0) && Date.now()>=(slot.cooldownUntil||0) && slot.temperature<35){BmsModel.clear(slot);if(st.cutOffLatched || (st.snapshot?.operatingState && st.snapshot.operatingState!=="HoatDong"))slot.isLocked=true;}
        else if(!cleared){slot.cooldownStartedAt=slot.cooldownStartedAt||start;slot.cooldownUntil=Math.max(slot.cooldownUntil||0,until);}
      }
      BmsModel.protect(slot);
    }
    persist(st.id);
  }
  function updateSafety(st) {
    for(const slot of st.slots) BmsModel.protect(slot);
    for(const command of Object.values(st.commands)){
      if(!command.waitingForCooling)continue;
      if(command.automatic && st.slots.find(x=>x.id===command.slotNumber)?.requiresAdmin)continue;
      const slot=st.slots.find(x=>x.id===command.slotNumber);
      if(slot && backend && MqttSync.connected() && st.snapshot?.status?.online && BmsModel.ready(slot) && (!command.batteryId||slot.batteryId===command.batteryId)){
        BmsModel.clear(slot);Object.assign(slot,{isDoorOpen:false,isEjected:false,isLocked:!!command.preserveStationLock || !!st.cutOffLatched || !!(st.snapshot?.operatingState && st.snapshot.operatingState!=="HoatDong")});
        command.waitingForCooling=false;command.success=true;
      }
    }
    persist(st.id);
  }
  const stateNames = {
    AwaitingOldBattery: "Chờ cắm pin cũ",
    Dispensing: "Đã chốt phí — chờ lấy pin mới",
    NeedsAssistance: "Cần kỹ thuật viên hỗ trợ",
    ClosingCancelled: "Đang đóng ngăn để hủy",
    ClosingExpired: "Hết thời gian — đang đóng ngăn",
    Completed: "Đổi pin hoàn tất",
    Cancelled: "Đã hủy",
    Expired: "Phiên đã hết hạn",
    ReturningOldBattery: "Chờ nhận lại pin cũ",
    Returned: "Đã trả lại pin cũ — chưa thu phí",
  };

  function setActiveStation(stId, animateCamera = false) {
    if (!stationData[stId]) return;
    activeStationId = stId;

    // Update switcher tab visual state
    const btnBk = $("btn-station-bk");
    const btnCg = $("btn-station-cg");
    if (btnBk && btnCg) {
      btnBk.classList.toggle("active", stId === "TRAM-BK01");
      btnCg.classList.toggle("active", stId === "TRAM-CG02");
    }
    const asideBk = $("aside-btn-bk");
    const asideCg = $("aside-btn-cg");
    if (asideBk && asideCg) {
      asideBk.classList.toggle("active", stId === "TRAM-BK01");
      asideCg.classList.toggle("active", stId === "TRAM-CG02");
    }

    if (animateCamera && view && typeof TWEEN !== "undefined") {
      const posX = stationData[stId].posX;
      new TWEEN.Tween(view.controls.target)
        .to({ x: posX, y: 0.2, z: 0 }, 600)
        .easing(TWEEN.Easing.Cubic.Out)
        .start();

      new TWEEN.Tween(view.camera.position)
        .to({ x: posX, y: 0.2, z: 4.8 }, 600)
        .easing(TWEEN.Easing.Cubic.Out)
        .start();
    }

    render();
  }

  function render() {
    const st = stationData[activeStationId];
    if (!st) return;

    if($("reset-simulation"))$("reset-simulation").disabled=(!st.leased&&!localStorage.getItem("bss-reset:"+st.id))||st.resetInFlight||!backend||!MqttSync.connected();
    if($("reset-status"))$("reset-status").textContent=st.resetting?"Trạm tạm dừng vì chưa xác nhận kết quả reset. Đăng nhập Admin và bấm Reset Simulation để thử lại cùng yêu cầu.":"";

    const s = st.slots.find((t) => t.id === st.selectedSlot) || st.slots[0];
    if (!s) return;

    // Update aside header
    const titleEl = $("station-title");
    if (titleEl) {
      titleEl.innerHTML = `${st.shortName} <span class="station-badge ${st.id === 'TRAM-BK01' ? 'badge-bk' : 'badge-cg'}">${backend && MqttSync.connected() && st.snapshot?.status?.online ? "Trực tuyến" : "Chưa đồng bộ"}</span>`;
    }
    const addrEl = $("station-address");
    if (addrEl) addrEl.textContent = st.address;

    const slotTitleEl = $("slot-title");
    if (slotTitleEl) {
      slotTitleEl.textContent = `Ngăn ${s.id} • ${s.batteryId || "Trống"}`;
    }

    const metrics = [
      ["Mức pin", s.hasBattery ? s.soc.toFixed(1) + "%" : "—"],
      ["Sức khỏe", s.hasBattery ? s.soh.toFixed(1) + "%" : "—"],
      ["Nhiệt độ", s.temperature.toFixed(1) + " °C"],
      ["Điện áp", s.voltage.toFixed(1) + " V"],
      ["Dòng sạc", s.current.toFixed(1) + " A"],
      ["Cửa hộc", s.isDoorOpen ? "Mở" : "Đóng"],
      ["Khóa bảo trì", s.isLocked ? "Đang khóa" : "Bình thường"],
      ["Làm mát tự động", s.isCooling ? "Đang bật" : "Tắt"],
      ["Số lần quá nhiệt", `${s.incidentCount||0} / 4${s.requiresAdmin ? " — cần quản trị viên" : ""}`],
      ["Khóa an toàn (thời gian thực)", s.cooldownUntil && Date.now()<s.cooldownUntil ? `${Math.floor(Math.ceil((s.cooldownUntil-Date.now())/1000)/60)}:${String(Math.ceil((s.cooldownUntil-Date.now())/1000)%60).padStart(2,"0")} còn lại — cấm cắm/rút` : BmsModel.locked(s) ? s.temperature>=35 ? "Đã hết 5 phút — tiếp tục làm mát" : s.requiresAdmin ? "Chờ quản trị viên xác nhận" : "Chờ máy chủ tự phục hồi" : "Không khóa"],
    ];

    const metricsEl = $("metrics");
    if (metricsEl) {
      metricsEl.replaceChildren(
        ...metrics.flatMap(([a, b]) => {
          let dt = document.createElement("dt");
          let dd = document.createElement("dd");
          dt.textContent = a;
          dd.textContent = b;
          return [dt, dd];
        })
      );
    }

    const gridEl = $("slots-grid");
    if (gridEl) {
      gridEl.replaceChildren(
        ...st.slots.map((t) => {
          let b = document.createElement("button");
          b.textContent = `${t.id} · ${t.hasBattery ? Math.round(t.soc) + "%" : "—"}`;
          b.className = t.id === st.selectedSlot ? "selected" : "";
          b.style.borderColor = t.isOverheated
            ? "#ef4444"
            : t.isLocked
            ? "#fbbf24"
            : t.soc >= 90
            ? "#34d399"
            : "#40536a";
          b.setAttribute("aria-label", `Chọn ngăn ${t.id} trạm ${st.id}`);
          b.onclick = () => {
            st.selectedSlot = t.id;
            render();
          };
          return b;
        })
      );
    }

    // Kiosk Area
    const stNameEl = $("kiosk-station-name");
    if (stNameEl) {
      stNameEl.textContent = `${st.name} (${st.id})`;
    }

    const walletEl = $("wallet-balance");
    if (walletEl) {
      walletEl.textContent = user
        ? `Ví hiện tại: ${Number($("flow-info").dataset.balance || 0).toLocaleString("vi-VN")} đ`
        : "";
    }

    const userNameEl = $("kiosk-user-name");
    if (userNameEl) {
      userNameEl.textContent = user ? user.name : "Khách hàng";
    }

    const loginSec = $("kiosk-login-section");
    const activeSec = $("kiosk-active-section");
    if (loginSec && activeSec) {
      loginSec.style.display = user ? "none" : "block";
      activeSec.style.display = user ? "block" : "none";
    }

    const active = session && !["Completed", "Cancelled", "Expired", "Returned"].includes(session.state);
    const startSwapBtn = $("start-swap");
    if (startSwapBtn) {
      startSwapBtn.hidden = !user || !!active;
      startSwapBtn.disabled = st.resetting || !st.leased || !backend || !MqttSync.connected() || currentFee === null;
      startSwapBtn.textContent = currentFee === null ? "Đang tải phí dịch vụ…" : `BẮT ĐẦU ĐỔI PIN (${currentFee.toLocaleString("vi-VN")} đ)`;
    }
    const cancelSwapBtn = $("cancel-swap");
    if (cancelSwapBtn) {
      cancelSwapBtn.hidden = !user || !active;
    }

    const stepBadge = $("step-badge");
    const stepMainText = $("step-main-text");
    if (stepBadge && stepMainText) {
      if (session) {
        stepBadge.textContent = `BƯỚC: ${stateNames[session.state] || session.state}`;
        stepMainText.textContent = `${session.oldBatteryId} → Hộc #${session.returnSlot} / Lấy ${session.newBatteryId} tại Hộc #${session.receiveSlot}`;
      } else if (user) {
        stepBadge.textContent = "BƯỚC 1: SẴN SÀNG";
        stepMainText.textContent = `Sẵn sàng thực hiện đổi pin tại ${st.id}`;
      }
    }

    const flowInfoEl = $("flow-info");
    if (flowInfoEl) {
      if (session) {
        flowInfoEl.textContent = `Phí dịch vụ: ${session.fee.toLocaleString("vi-VN")} đ · ${session.charged ? "Đã trừ ví" : "Giữ chỗ phí"}. ${session.note || ""}`;
      } else if (user) {
        flowInfoEl.textContent = currentFee === null ? "Đang tải chính sách phí." : `Phí dịch vụ: ${currentFee.toLocaleString("vi-VN")} đ / lượt. Xác nhận để giữ chỗ ví và pin.`;
      }
    }

    const action = $("physical-action");
    action.hidden = true;
    action.disabled = st.resetting || !st.leased || !backend || !MqttSync.connected();

    const physicalSession = (st.snapshot?.sessions || []).find(
      (x) => x.state === "ReturningOldBattery" || x.state === "AwaitingOldBattery" || (x.charged && ["Dispensing", "NeedsAssistance"].includes(x.state))
    );

    if (physicalSession) {
      if (physicalSession.state === "ReturningOldBattery") {
        const t = st.slots.find((x) => x.id === physicalSession.returnSlot);
        if (t?.isDoorOpen && t.batteryId === physicalSession.oldBatteryId && !t.isLocked && !BmsModel.locked(t) && t.temperature <= 45) {
          action.hidden = false;
          action.textContent = `[MÔ PHỎNG] Nhận lại pin ${t.batteryId} và đóng ngăn #${t.id}`;
          action.onclick = () => returnOld(st.id, physicalSession);
        }
      } else if (physicalSession.state === "AwaitingOldBattery") {
        const t = st.slots.find((x) => x.id === physicalSession.returnSlot);
        if (t?.isDoorOpen && !t.hasBattery && !t.isLocked && !BmsModel.locked(t)) {
          action.hidden = false;
          action.textContent = `👉 [MÔ PHỎNG] Cắm pin ${physicalSession.oldBatteryId} vào Hộc #${t.id} & Đóng hộc`;
          action.onclick = () => insertOld(st.id, physicalSession);
        }
      } else {
        const t = st.slots.find((x) => x.id === physicalSession.receiveSlot);
        if (t?.isEjected && !t.isLocked && !BmsModel.locked(t) && t.temperature <= 45) {
          action.hidden = false;
          action.textContent = `👉 [MÔ PHỎNG] Rút pin mới ${physicalSession.newBatteryId} từ Hộc #${t.id} & Đóng hộc`;
          action.onclick = () => takeNew(st.id, physicalSession);
        }
      }
    }

    const replacement = Object.entries(st.commands).find(([,c]) => c.waitingReplacement);
    const replaceAction = $("replacement-action");
    replaceAction.hidden = !replacement;
    replaceAction.disabled = st.resetting || !st.leased || !backend || !MqttSync.connected();
    if(replacement) {
      const [id,c] = replacement;
      replaceAction.textContent = `[MÔ PHỎNG] Tháo pin cũ, lắp ${c.batteryId} vào ngăn #${c.slotNumber}, đóng cửa`;
      replaceAction.onclick = () => {
        if(st.resetting || !st.leased || !backend || !MqttSync.connected() || !c.waitingReplacement) return;
        const slot = st.slots.find(x=>x.id===c.slotNumber);
        const battery = st.snapshot?.batteries.find(p=>p.maPin===c.batteryId);
        if(!slot || BmsModel.locked(slot) || !battery || slot.temperature>=35 || battery.nhietDo>=35 || battery.soH<80 ||
          STATION_IDS.some(id=>stationData[id].slots.some(t=>t.batteryId===c.batteryId))) return;
        Object.assign(slot,{batteryId:battery.maPin,hasBattery:true,soc:battery.soC,soh:battery.soH,
          designCapacityAh:battery.dungLuongThietKe||30,temperature:battery.nhietDo,voltage:battery.dienAp,current:0,isOverheated:false,isLocked:false,
          isDoorOpen:false,isEjected:false,isCharging:false});
        c.waitingReplacement=false; persist(st.id); ack(st.id,id); visuals(st.id,slot); render();
      };
    }

    $("overheat").disabled = st.resetting || !st.leased || !s.hasBattery;
    $("cool").disabled = st.resetting || !st.leased || !s.hasBattery;

    const simTimeEl = $("sim-time");
    if (simTimeEl) {
      simTimeEl.textContent = `Thời gian mô phỏng ${Math.floor(simSeconds / 60)}:${String(Math.floor(simSeconds % 60)).padStart(2, "0")}`;
    }
  }

  function visuals(stationId, s) {
    CabinetModel.setBatteryPresence(stationId, s.id, s.hasBattery, false);
    CabinetModel.setBatteryEjected(stationId, s.id, !!s.isEjected, true);
    CabinetModel.setSlotLedColor(
      stationId,
      s.id,
      s.isOverheated
        ? 0xef4444
        : s.isLocked
        ? 0xfbbf24
        : !s.hasBattery
        ? 0x64748b
        : s.soc >= 90
        ? 0x84cc16
        : 0xf59e0b,
      1.8
    );
    CabinetModel.updateBatteryDisplay(stationId, s.id, s.soc, s.isCharging);
    CabinetModel.setAlarmLight(stationId, s.id, s.isOverheated);

    const emitterKey = `${stationId}_${s.id}`;
    const stObj = CabinetModel.getStation(stationId);
    const slotObj = stObj?.slots.find((x) => x.slotId === s.id);

    if (s.isOverheated && slotObj && !ParticleSystem.isOverheating(emitterKey)) {
      ParticleSystem.startOverheat(emitterKey, slotObj.smokeAnchor);
    }
    if (!s.isOverheated) {
      ParticleSystem.stopOverheat(emitterKey);
    }
  }

  function sendEvent(stationId, event, s, slotId, battery) {
    const st = stationData[stationId];
    if (!st) return;
    st.slots.forEach(t=>BmsModel.protect(t));
    st.pending.push({ event, sessionId: s.id, slotNumber: slotId, batteryId: battery });
    persist(stationId);
    flush(stationId);
  }

  function insertOld(stationId, s) {
    const st = stationData[stationId];
    if (!st || st.resetting || st.pending.some((e) => e.sessionId === s.id && e.event === "OLD_INSERTED")) return;
    if (!backend || !MqttSync.connected() || !st.snapshot?.sessions.some(x=>x.id===s.id && x.state==="AwaitingOldBattery")) return;
    const t = st.slots.find((x) => x.id === s.returnSlot);
    if (!t?.isDoorOpen || t.hasBattery || t.isLocked || BmsModel.locked(t)) return;

    const old = st.snapshot?.batteries.find((p) => p.maPin === s.oldBatteryId);
    if (!old) {
      notice("Không đọc được BMS pin trả.");
      return;
    }
    Object.assign(t, {
      hasBattery: true,
      batteryId: s.oldBatteryId,
      designCapacityAh: old.dungLuongThietKe||30,
      soc: old.soC,
      soh: old.soH,
      temperature: old.nhietDo,
      voltage: old.dienAp,
      current: 0,
      isDoorOpen: false,
      isCharging: false,
    });
    sendEvent(stationId, "OLD_INSERTED", s, t.id, t.batteryId);
    visuals(stationId, t);
    render();
  }

  function takeNew(stationId, s) {
    const st = stationData[stationId];
    if (!st || st.resetting || st.pending.some((e) => e.sessionId === s.id && e.event === "NEW_TAKEN")) return;
    if (!backend || !MqttSync.connected() || !st.snapshot?.sessions.some(x=>x.id===s.id && x.charged && ["Dispensing","NeedsAssistance"].includes(x.state))) return;
    const t = st.slots.find((x) => x.id === s.receiveSlot);
    if (!t?.isEjected || t.isLocked || BmsModel.locked(t) || t.temperature > 45) return;

    Object.assign(t, {
      hasBattery: false,
      batteryId: null,
      soc: 0,
      current: 0,
      voltage: 0,
      isDoorOpen: false,
      isCharging: false,
      isEjected: false,
    });
    sendEvent(stationId, "NEW_TAKEN", s, t.id, s.newBatteryId);
    visuals(stationId, t);
    render();
  }

  function returnOld(stationId, session) {
    const st = stationData[stationId];
    const slot = st?.slots.find(x => x.id === session.returnSlot);
    if (!st?.snapshot?.sessions.some(x => x.id === session.id && x.state === "ReturningOldBattery")) return;
    if (st?.resetting || !st?.leased || !backend || !MqttSync.connected() || !slot?.isDoorOpen || slot.isLocked || BmsModel.locked(slot) || slot.temperature > 45 || slot.batteryId !== session.oldBatteryId) return;
    if (st.pending.some(e => e.sessionId === session.id && e.event === "OLD_RETURNED")) return;
    Object.assign(slot, {hasBattery:false,batteryId:null,soc:0,current:0,voltage:0,isDoorOpen:false,isCharging:false,isEjected:false});
    sendEvent(stationId, "OLD_RETURNED", session, slot.id, session.oldBatteryId);
    visuals(stationId, slot); render();
  }

  function handleCommand(c) {
    const targetStation = c.stationId || activeStationId;
    const st = stationData[targetStation];
    if (!st || !st.leased || st.resetting) return;

    if (st.commands[c.commandId]) {
      if(st.commands[c.commandId].waitingForCooling){const slot=st.slots.find(x=>x.id===c.slotNumber);if(slot)slot.isCooling=true;}
      ack(targetStation, c.commandId);
      return;
    }

    let success = true;
    let waitingForCooling = false;
    const s = st.slots.find((t) => t.id === c.slotNumber);

    if(["UNLOCK_SLOT","EJECT_BATTERY","RETURN_OLD_BATTERY","REPLACE_BATTERY","RESTORE_STATION"].includes(c.command) && (c.command==="RESTORE_STATION"?st.slots.some(t=>BmsModel.locked(t)):BmsModel.locked(s))){
      st.commands[c.commandId]={success:false,pending:true,slotNumber:c.slotNumber,batteryId:c.batteryId,sessionId:c.sessionId};persist(targetStation);ack(targetStation,c.commandId);return;
    }
    switch (c.command) {
      case "UNLOCK_SLOT":
        success = !!s && !s.isLocked && s.temperature < 55;
        if (success) {
          s.isDoorOpen = true;
          s.current = 0;
          s.isCharging = false;
        }
        break;
      case "CLOSE_SLOT":
        if (s) s.isDoorOpen = false;
        else success = false;
        break;
      case "LOCK_SLOT":
        if (s) {
          s.isLocked = true;
          s.isDoorOpen = false;
          s.current = 0;
        } else success = false;
        break;
      case "EJECT_BATTERY":
        success =
          !!s &&
          s.batteryId === c.batteryId &&
          !s.isLocked &&
          s.temperature <= 45 &&
          s.soc >= 90 &&
          s.soh >= 80;
        if (success) {
          s.isEjected = true;
          s.isDoorOpen = true;
          s.current = 0;
          s.isCharging = false;
        }
        break;
      case "CUT_OFF":
        st.cutOffLatched=true;
        st.slots.forEach((t) =>
          Object.assign(t, {
            isLocked: true,
            isDoorOpen: false,
            isEjected: false,
            current: 0,
            isCharging: false,
          })
        );
        break;
      case "RESTORE_STATION":
        success = st.slots.every((t) => t.temperature < 35);
        if (success) {
          st.cutOffLatched=false;
          st.slots.forEach((t) =>
            Object.assign(t, {
              isLocked: false,
              isDoorOpen: false,
              isOverheated: false,
            })
          );
        }
        break;
      case "TRIGGER_OVERHEAT":
        if (s) {
          s.temperature = 65;
          BmsModel.step(s, 0);
        } else success = false;
        break;
      case "AUTO_RESET_ALARM":
      case "RESET_ALARM":
        success = !!s && s.hasBattery && (!c.batteryId || c.batteryId === s.batteryId) && (c.command !== "AUTO_RESET_ALARM" || !s.requiresAdmin);
        if (success) {
          s.isCooling = true; s.isLocked = true; s.isDoorOpen = false; s.isEjected = false; s.current = 0;
          waitingForCooling = true;
        }
        break;
      case "RETURN_OLD_BATTERY":
        success = !!s && s.hasBattery && s.batteryId === c.batteryId && s.temperature <= 45;
        if (success) Object.assign(s,{isLocked:false,isDoorOpen:true,isCharging:false,current:0,isEjected:false});
        break;
      case "REPLACE_BATTERY": {
        const battery = st.snapshot?.batteries.find(p => p.maPin === c.batteryId);
        success = !!s && !!battery && s.temperature < 35 && battery.nhietDo < 35 && battery.soH >= 80 &&
          !STATION_IDS.some(id => stationData[id].slots.some(t => t.batteryId === c.batteryId));
        break;
      }
      case "QUERY_STATUS":
        if (st.commands[c.batteryId]) ack(targetStation, c.batteryId);
        break;
      default:
        success = false;
    }

    st.commands[c.commandId] = {
      success,
      waitingForCooling,
      automatic: c.command === "AUTO_RESET_ALARM",
      preserveStationLock: !!c.preserveStationLock,
      waitingReplacement: success && c.command === "REPLACE_BATTERY",
      sessionId: c.sessionId,
      slotNumber: c.slotNumber,
      batteryId: c.batteryId,
      pending: true,
    };
    persist(targetStation);
    st.slots.forEach((slot) => visuals(targetStation, slot));
    ack(targetStation, c.commandId);
    render();
  }

  function ack(stationId, id) {
    const st = stationData[stationId];
    const c = st?.commands[id];
    if (c && !c.waitingForCooling && !c.waitingReplacement) {
      MqttSync.publish(stationId, "acks", st.slots, {
        event: "ACK",
        commandId: id,
        sessionId: c.sessionId,
        slotNumber: c.slotNumber,
        batteryId: c.batteryId,
        success: c.success,
      });
    }
  }

  function flush(stationId) {
    const ids = stationId ? [stationId] : STATION_IDS;
    for (const id of ids) {
      const st = stationData[id];
      if (!st || !st.leased || st.resetting) continue;

      MqttSync.publish(id, "telemetry", st.slots);
      for (const [cmdId, c] of Object.entries(st.commands)) {
        if (c.pending) ack(id, cmdId);
      }
      for (const event of st.pending) {
        MqttSync.publish(id, "events", st.slots, event);
      }
    }
  }

  async function snapshotRefresh(stationId) {
    const st = stationData[stationId];
    if (!st) return;

    if(st.resetting)return;
    const epoch=st.resetEpoch||0;
    const next = await MqttSync.json(`device/${stationId}/snapshot`);
    if(st.resetting || epoch!==(st.resetEpoch||0))return;
    st.snapshot = next;

    if (MqttSync.leaseId(stationId) && next.status?.ownerId && next.status.ownerId !== MqttSync.clientId()) {
      st.leased = false;
      notice(`Trạm ${stationId} đã được một simulator khác tiếp quản.`);
    }

    if (!st.slots.length) {
      st.slots = next.slots.map((n) => ({
        id: n.soThuTu,
        batteryId: n.maPinHienTai,
        hasBattery: !!n.maPinHienTai,
        designCapacityAh: next.batteries.find(p=>p.maPin===n.maPinHienTai)?.dungLuongThietKe || 30,
        soc: n.soC,
        soh: n.soH,
        temperature: n.nhietDo,
        voltage: n.dienAp,
        current: n.dongDien,
        isCharging: n.dongDien > 0,
        isDoorOpen: n.cuaDangMo,
        isLocked: n.khoaBaoTri,
        isOverheated: n.nhietDo >= 55 || n.trangThaiPin === "LoiQuaNhiet",
        isEjected: false,
      }));

      for (const e of st.pending) {
        const t = st.slots.find((x) => x.id === e.slotNumber);
        if (!t) continue;
        if (e.event === "OLD_INSERTED") {
          const old = next.batteries.find((p) => p.maPin === e.batteryId);
          if (old) {
            Object.assign(t, {
              hasBattery: true,
              batteryId: e.batteryId,
              designCapacityAh: old.dungLuongThietKe||30,
              soc: old.soC,
              soh: old.soH,
              temperature: old.nhietDo,
              isDoorOpen: false,
            });
          }
        }
        if (e.event === "NEW_TAKEN" || e.event === "OLD_RETURNED") {
          Object.assign(t, { hasBattery: false, batteryId: null, soc: 0, current: 0, isEjected: false });
        }
      }

      for (const s of next.sessions) {
        if (s.charged) {
          const t = st.slots.find((x) => x.id === s.receiveSlot);
          if (
            t &&
            next.commands.some(
              (c) => c.sessionId === s.id && c.command === "EJECT_BATTERY" && c.status === "Completed"
            )
          ) {
            t.isEjected = true;
            t.isDoorOpen = true;
            t.current = 0;
          }
        }
      }
      st.slots.forEach((slot) => visuals(stationId, slot));
    }
    syncCooldowns(st,next.cooldowns||[]);

    for(const command of Object.values(st.commands)){
      if(command.waitingForCooling){const slot=st.slots.find(x=>x.id===command.slotNumber);if(slot)slot.isCooling=true;}
    }

    st.pending = st.pending.filter((e) => {
      const s = next.sessions.find((x) => x.id === e.sessionId);
      return !!s && (e.event === "OLD_INSERTED" ? !s.oldInserted : !["Completed", "Returned"].includes(s.state));
    });

    for (const c of next.commands) {
      if (st.commands[c.commandId] && ["Completed", "Failed"].includes(c.status)) {
        st.commands[c.commandId].pending = false;
        st.commands[c.commandId].waitingForCooling = false;
        st.commands[c.commandId].waitingReplacement = false;
      }
    }

    persist(stationId);
    return next;
  }

  async function refresh() {
    if (updating) return;
    updating = true;

    try {
      for (const id of STATION_IDS) {
        await snapshotRefresh(id);
      }
      backend = true;
      if (user) { const price = await api("pricing"); currentFee = Number(price.fee); }

      // Active station commands check for dispensing
      const activeSt = stationData[activeStationId];
      if (activeSt?.snapshot) {
        for (const c of activeSt.snapshot.commands) {
          if (
            c.command === "EJECT_BATTERY" &&
            c.status === "Completed" &&
            activeSt.snapshot.sessions.some(
              (s) => s.id === c.sessionId && ["Dispensing", "NeedsAssistance"].includes(s.state)
            )
          ) {
            const t = activeSt.slots.find((x) => x.id === c.slotNumber);
            if (t?.hasBattery && !t.isLocked) t.isEjected = true;
          }
        }
      }

      if (user) {
        const all = await api("swaps");
        session =
          all.find((s) => !["Completed", "Cancelled", "Expired", "Returned"].includes(s.state)) ||
          all[0] ||
          null;

        if (session && ["Completed", "Cancelled", "Expired", "Returned"].includes(session.state)) {
          sessionStorage.removeItem("bss-request");
        }
        const wallet = await api("wallet");
        $("flow-info").dataset.balance = wallet?.SoDuVi ?? 0;
      }

      const bothLeased = STATION_IDS.every((id) => stationData[id].leased);
      const connEl = $("connection");
      if (connEl) {
        connEl.textContent = bothLeased
          ? MqttSync.connected()
            ? "Đồng bộ 2 trạm • Backend + MQTT trực tuyến"
            : "Backend kết nối • Đang chờ MQTT"
          : "Đang kết nối thiết bị mô phỏng 2 trạm…";
      }
      render();
    } catch (e) {
      if (e.status === 401) {
        user = null;
        token = null;
        session = null;
        sessionStorage.removeItem("bss-token");
        backend = true;
        const connEl = $("connection");
        if (connEl) connEl.textContent = "Backend đã kết nối • cần đăng nhập lại";
        notice("Phiên đăng nhập hết hạn. Dữ liệu đổi pin vẫn được lưu tại server.");
      } else {
        backend = false;
        const connEl = $("connection");
        if (connEl) connEl.textContent = "Mất kết nối backend — kiểm tra server";
        notice(e.message);
      }
      render();
    } finally {
      updating = false;
    }
  }

  async function acquire() {
    try {
      const leases = await MqttSync.connect(STATION_IDS, handleCommand, (ok, text) => {
        const connEl = $("connection");
        if (connEl) connEl.textContent = text;
      });
      for (const id of STATION_IDS) {
        stationData[id].leased = !!leases[id]?.leaseId;
      }
    } catch (e) {
      notice(e.message);
      setTimeout(() => {
        if (!STATION_IDS.every((id) => stationData[id].leased)) acquire();
      }, 5000);
    }
  }

  function setupScene() {
    view = SceneView.init($("canvas-container"));
    CabinetModel.buildCabinet(view.scene);
    ParticleSystem.init(view.scene);

    const resize = () => {
      let w = $("canvas-container").clientWidth;
      let h = $("canvas-container").clientHeight;
      view.camera.aspect = w / h;
      view.camera.updateProjectionMatrix();
      view.renderer.setSize(w, h);
    };
    window.addEventListener("resize", resize);
    resize();

    // Camera helper functions
    function zoomToKiosk(stId) {
      const posX = stationData[stId]?.posX ?? (stId === "TRAM-BK01" ? -1.55 : 1.55);
      if (typeof TWEEN !== "undefined" && view) {
        new TWEEN.Tween(view.camera.position)
          .to({ x: posX, y: 1.15, z: 2.2 }, 650)
          .easing(TWEEN.Easing.Cubic.Out)
          .start();
        new TWEEN.Tween(view.controls.target)
          .to({ x: posX, y: 0.95, z: 0.6 }, 650)
          .easing(TWEEN.Easing.Cubic.Out)
          .start();
      }
    }

    function zoomToOverview() {
      if (typeof TWEEN !== "undefined" && view) {
        new TWEEN.Tween(view.camera.position)
          .to({ x: 0, y: 1.6, z: 7.5 }, 650)
          .easing(TWEEN.Easing.Cubic.Out)
          .start();
        new TWEEN.Tween(view.controls.target)
          .to({ x: 0, y: 0.4, z: 0 }, 650)
          .easing(TWEEN.Easing.Cubic.Out)
          .start();
      }
    }

    function openKioskModal(stId) {
      const targetStation = stId || activeStationId;
      if (targetStation !== activeStationId) {
        setActiveStation(targetStation, false);
      }
      zoomToKiosk(activeStationId);
      const modal = $("kiosk-modal");
      if (modal) modal.style.display = "flex";
      render();
    }

    function closeKioskModal() {
      const modal = $("kiosk-modal");
      if (modal) modal.style.display = "none";
      zoomToOverview();
    }

    window.openKioskModal = openKioskModal;
    window.closeKioskModal = closeKioskModal;

    // 3D Pointer Hover: Change cursor to pointer when hovering over Kiosk Touchscreen or Slots
    view.renderer.domElement.addEventListener("pointermove", (e) => {
      let rect = e.currentTarget.getBoundingClientRect();
      let ray = new THREE.Raycaster();
      ray.setFromCamera(
        new THREE.Vector2(((e.clientX - rect.left) / rect.width) * 2 - 1, -((e.clientY - rect.top) / rect.height) * 2 + 1),
        view.camera
      );
      let isHoverInteractive = false;
      for (const hit of ray.intersectObjects(view.scene.children, true)) {
        let obj = hit.object;
        while (obj) {
          if (obj.userData?.isKiosk || obj.userData?.slotId) {
            isHoverInteractive = true;
            break;
          }
          obj = obj.parent;
        }
        if (isHoverInteractive) break;
      }
      view.renderer.domElement.style.cursor = isHoverInteractive ? "pointer" : "default";
    });

    // 3D Pointer/Click Raycasting: Touch kiosk or select slot directly in 3D
    let down = null;
    view.renderer.domElement.addEventListener("pointerdown", (e) => (down = { x: e.clientX, y: e.clientY }));
    view.renderer.domElement.addEventListener("pointerup", (e) => {
      if (!down || Math.hypot(e.clientX - down.x, e.clientY - down.y) > 6) return;
      let rect = e.currentTarget.getBoundingClientRect();
      let ray = new THREE.Raycaster();
      ray.setFromCamera(
        new THREE.Vector2(((e.clientX - rect.left) / rect.width) * 2 - 1, -((e.clientY - rect.top) / rect.height) * 2 + 1),
        view.camera
      );

      for (const hit of ray.intersectObjects(view.scene.children, true)) {
        let obj = hit.object;
        let isKioskHit = false;
        let hitStation = null;
        let hitSlot = null;
        while (obj) {
          if (obj.userData?.isKiosk) {
            isKioskHit = true;
            hitStation = obj.userData.stationId;
            break;
          }
          if (obj.userData?.stationId) {
            hitStation = hitStation || obj.userData.stationId;
            hitSlot = hitSlot || obj.userData.slotId;
          }
          obj = obj.parent;
        }

        if (isKioskHit) {
          openKioskModal(hitStation || activeStationId);
          return;
        }

        if (hitStation) {
          if (hitStation !== activeStationId) {
            setActiveStation(hitStation, false);
          }
          if (hitSlot) {
            stationData[hitStation].selectedSlot = hitSlot;
          }
          render();
          return;
        }
      }
    });

    let clock = new THREE.Clock();
    function animate() {
      requestAnimationFrame(animate);
      const dt = clock.getDelta();
      if (typeof TWEEN !== "undefined") TWEEN.update();
      ParticleSystem.update(dt);
      view.controls.update();
      view.renderer.render(view.scene, view.camera);
    }
    animate();
  }

  function tick(elapsed) {
    for(const id of STATION_IDS){
      const st=stationData[id];
      if(!st.leased || st.resetting)continue;
      for(const slot of st.slots){BmsModel.step(slot,elapsed,scale);visuals(id,slot);}
      updateSafety(st);flush(id);
    }
    simSeconds+=elapsed*scale;
    render();
  }

  async function init() {
    setupScene();

    // Station Switcher Button listeners (Header & Aside)
    const btnBk = $("btn-station-bk");
    if (btnBk) btnBk.onclick = () => setActiveStation("TRAM-BK01", true);

    const btnCg = $("btn-station-cg");
    if (btnCg) btnCg.onclick = () => setActiveStation("TRAM-CG02", true);

    const asideBk = $("aside-btn-bk");
    if (asideBk) asideBk.onclick = () => setActiveStation("TRAM-BK01", true);

    const asideCg = $("aside-btn-cg");
    if (asideCg) asideCg.onclick = () => setActiveStation("TRAM-CG02", true);

    // Kiosk Modal Close & Open listeners
    const closeBtn = $("close-kiosk-modal");
    if (closeBtn) closeBtn.onclick = closeKioskModal;

    const modalOverlay = $("kiosk-modal");
    if (modalOverlay) {
      modalOverlay.onclick = (e) => {
        if (e.target === modalOverlay) closeKioskModal();
      };
    }

    const asideOpenKiosk = $("aside-open-kiosk");
    if (asideOpenKiosk) {
      asideOpenKiosk.onclick = () => openKioskModal(activeStationId);
    }

    const btnQuickLogin = $("btn-quick-login");
    if (btnQuickLogin) {
      btnQuickLogin.onclick = () => {
        const u = $("username");
        const p = $("password");
        if (u) u.value = "driver01";
        if (p) p.value = "123456";
        notice("Đã điền tài khoản demo. Bấm Đăng nhập để xác thực bằng mật khẩu.");
      };
    }

    $("time-scale").onchange = (e) => (scale = Number(e.target.value));

    $("reset-form").onsubmit=async(e)=>{
      e.preventDefault();
      try {
        const login=await api("auth/login","POST",{username:$("reset-username").value,password:$("reset-password").value});
        if(login.user.role!=="Admin")throw Error("Reset Simulation yêu cầu tài khoản Admin.");
        $("reset-password").value="";
        await resetSimulation(login.token);
      } catch(error){notice(error.message);}
    };

    $("login-form").onsubmit = async (e) => {
      e.preventDefault();
      try {
        const r = await api("auth/login", "POST", {
          username: $("username").value,
          password: $("password").value,
        });
        if (r.user.role !== "KhachHang")
          throw Error("Kiosk yêu cầu tài khoản khách hàng. Quản trị dùng Desktop.");
        token = r.token;
        user = r.user;
        sessionStorage.setItem("bss-token", token);
        notice("Đăng nhập thành công.");
        await refresh();
      } catch (err) {
        notice(err.message);
      }
    };

    $("start-swap").onclick = async () => {
      const st = stationData[activeStationId];
      if (!st || st.resetting || !st.leased || !backend || currentFee === null) return;
      if (!confirm(`Đổi pin tại ${activeStationId} với phí ${currentFee.toLocaleString("vi-VN")} đ?`)) return;
      let key = sessionStorage.getItem("bss-request") || crypto.randomUUID();
      sessionStorage.setItem("bss-request", key);
      try {
        session = await api("swaps", "POST", { stationId: activeStationId, idempotencyKey: key });
        notice(`Đã giữ chỗ tại ${activeStationId}. Chờ ngăn nhận pin mở.`);
        await refresh();
      } catch (err) {
        notice(err.message);
      }
    };

    $("cancel-swap").onclick = async () => {
      try {
        if (session) session = await api(`swaps/${session.id}/cancel`, "POST", {});
        await refresh();
      } catch (err) {
        notice(err.message);
      }
    };

    $("logout").onclick = async () => {
      try {
        await fetch("/api/v1/auth/logout", {
          method: "POST",
          headers: { Authorization: "Bearer " + token },
        });
      } finally {
        token = null;
        user = null;
        session = null;
        sessionStorage.removeItem("bss-token");
        render();
      }
    };

    $("overheat").onclick = () => {
      const st = stationData[activeStationId];
      if (!st || !st.leased || st.resetting) return;
      let s = st.slots.find((t) => t.id === st.selectedSlot);
      if (!s?.hasBattery) return;

      s.temperature = 65;
      BmsModel.step(s, 0);
      MqttSync.publish(activeStationId, "alerts", st.slots, {
        event: "OVERHEAT",
        slotNumber: s.id,
        batteryId: s.batteryId,
      });
      visuals(activeStationId, s);
      render();
    };

    $("cool").onclick = () => {
      const st = stationData[activeStationId];
      if (!st || !st.leased || st.resetting) return;
      let s = st.slots.find((t) => t.id === st.selectedSlot);
      if (!s) return;

      s.isCooling = true;
      notice("Đã bật làm mát giả lập. Nhiệt độ giảm theo thời gian; ngăn vẫn khóa đến khi reset được xác nhận.");
      flush(activeStationId);
      render();
    };

    if (token) {
      try {
        user = await api("auth/me");
      } catch {
        token = null;
        sessionStorage.removeItem("bss-token");
      }
    }

    await refresh();
    await acquire();

    // Use elapsed monotonic time, including background-tab timer delays.
    let previousTick=performance.now();
    setInterval(() => {
      const now=performance.now(),elapsed=Math.max(0,(now-previousTick)/1000);previousTick=now;
      tick(elapsed);
    }, 1000);

    setInterval(refresh, 1000);
  }

  return { init };
})();
