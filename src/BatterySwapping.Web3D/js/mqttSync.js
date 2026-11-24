const MqttSync = (() => {
  let client = null;
  let sequence = 0;
  const leases = {}; // stationId -> lease object
  let stationList = ["TRAM-BK01", "TRAM-CG02"];
  const clientId = (typeof localStorage !== "undefined" && localStorage.getItem("bss-device-client")) ||
    (typeof sessionStorage !== "undefined" && sessionStorage.getItem("bss-device-client")) ||
    crypto.randomUUID();
  try {
    if (typeof localStorage !== "undefined") localStorage.setItem("bss-device-client", clientId);
    if (typeof sessionStorage !== "undefined") sessionStorage.setItem("bss-device-client", clientId);
  } catch {}

  if (typeof window !== "undefined" && window.addEventListener) {
    window.addEventListener("storage", (e) => {
      if (e.key && e.key.startsWith("bss-lease:") && e.newValue) {
        const st = e.key.replace("bss-lease:", "");
        try {
          leases[st] = JSON.parse(e.newValue);
        } catch {}
      }
    });
  }

  async function json(path, options = {}) {
    const r = await fetch("/api/v1/" + path, {
      ...options,
      headers: { "Content-Type": "application/json", ...(options.headers || {}) },
      signal: AbortSignal.timeout(8000),
    });
    if (!r.ok) {
      const e = await r.json().catch(() => ({}));
      const error = Error(e.error || `Lỗi kết nối ${r.status}`);
      error.status = r.status;
      throw error;
    }
    return r.json();
  }

  async function connect(stations, handler, onStatus) {
    stationList = Array.isArray(stations) ? stations : [stations];
    const config = await json("device/config");

    // Acquire device leases for all stations
    for (const st of stationList) {
      try {
        const pendingReset=JSON.parse(localStorage.getItem("bss-reset:"+st)||"null");
        if(pendingReset){leases[st]={leaseId:pendingReset.leaseId};continue;}
        const l = await json("device/lease", {
          method: "POST",
          body: JSON.stringify({ stationId: st, clientId }),
        });
        leases[st] = l;
        if (typeof localStorage !== "undefined") localStorage.setItem("bss-lease:" + st, JSON.stringify(l));
      } catch (err) {
        const savedLease = typeof localStorage !== "undefined" && JSON.parse(localStorage.getItem("bss-lease:" + st) || "null");
        if (savedLease) {
          leases[st] = savedLease;
        } else {
          console.warn(`Could not lease ${st}:`, err.message);
        }
      }
    }

    client = mqtt.connect(config.brokerUrl, {
      clientId: "bss_sim_" + clientId,
      username: config.username,
      password: config.password,
      clean: true,
      reconnectPeriod: 1000,
      connectTimeout: 4000,
    });

    client.on("connect", () => {
      // Subscribe to commands for all stations using MQTT wildcard +
      client.subscribe("bss/station/+/commands", { qos: 1 }, (err) => {
        onStatus(!err, err ? "Không đăng ký được MQTT" : "MQTT cục bộ đã kết nối (2 trạm)");
      });
    });

    client.on("close", () => onStatus(false, "MQTT mất kết nối"));
    client.on("error", () => onStatus(false, "Không kết nối được broker"));

    client.on("message", (topic, payload) => {
      try {
        const c = JSON.parse(payload.toString());
        const topicParts = topic.split("/");
        const topicStation = topicParts[2];
        const targetStation = c.stationId || topicStation;
        if (targetStation && c.schemaVersion === 1) {
          c.stationId = targetStation;
          handler(c);
        }
      } catch (e) {
        console.error("Command rejected", e.message);
      }
    });

    return leases;
  }

  function publish(targetStationOrKind, kindOrSlots, slotsOrExtra = [], extra = {}) {
    let stationId, kind, slots, additional;

    // Support both publish(stationId, kind, slots, extra) and legacy publish(kind, slots, extra)
    if (typeof targetStationOrKind === "string" && (targetStationOrKind.startsWith("TRAM-") || stationList.includes(targetStationOrKind))) {
      stationId = targetStationOrKind;
      kind = kindOrSlots;
      slots = slotsOrExtra || [];
      additional = extra || {};
    } else {
      stationId = stationList[0] || "TRAM-BK01";
      kind = targetStationOrKind;
      slots = kindOrSlots || [];
      additional = slotsOrExtra || {};
    }

    const lease = leases[stationId];
    if (!client?.connected || !lease) return false;

    const message = {
      schemaVersion: 1,
      stationId: stationId,
      leaseId: lease.leaseId,
      messageId: crypto.randomUUID(),
      sequence: ++sequence,
      timestamp: new Date().toISOString(),
      event: null,
      commandId: null,
      sessionId: null,
      slotNumber: null,
      batteryId: null,
      success: true,
      slots: slots.map((s) => ({
        slotNumber: s.id,
        batteryId: s.batteryId,
        soc: s.soc,
        soh: s.soh,
        temperature: s.temperature,
        voltage: s.voltage,
        current: s.current,
        isCharging: s.isCharging,
        isDoorOpen: s.isDoorOpen,
        isLocked: s.isLocked,
        isOverheated: !!s.isOverheated,
      })),
      ...additional,
    };

    client.publish(`bss/station/${stationId}/${kind}`, JSON.stringify(message), {
      qos: kind === "telemetry" ? 0 : 1,
      retain: false,
    });
    return true;
  }

  return {
    connect,
    publish,
    json,
    connected: () => !!client?.connected,
    clientId: () => clientId,
    leaseId: (st) => leases[st || stationList[0]]?.leaseId,
    applyLease: (st, leaseId) => {
      leases[st] = { ...leases[st], leaseId };
      try { if (typeof localStorage !== "undefined") localStorage.setItem("bss-lease:" + st, JSON.stringify(leases[st])); } catch {}
    },
    isLeased: (st) => !!leases[st]?.leaseId,
    disconnect: () => {
      client?.end(true);
      client = null;
    },
  };
})();
