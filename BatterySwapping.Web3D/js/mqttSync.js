/**
 * mqttSync.js
 * MQTT over WebSocket Client for BSS Digital Twin Telemetry & Commands
 * Broker Default: broker.emqx.io:8084 (Secure WSS) or local WebSocket
 */

const MqttSync = (function () {
  let client = null;
  let isConnected = false;
  let stationId = "TRAM-BK01";
  let commandCallback = null;
  let telemetryInterval = null;

  const DEFAULT_BROKER = "wss://broker.emqx.io:8084/mqtt";
  const TELEMETRY_TOPIC = `bss/station/${stationId}/telemetry`;
  const COMMAND_TOPIC = `bss/station/${stationId}/commands`;

  function init(onCommandReceived) {
    commandCallback = onCommandReceived;
  }

  function connect(brokerUrl = DEFAULT_BROKER, onStatusChange = null) {
    if (typeof mqtt === "undefined") {
      console.warn("[MQTT] Thư viện mqtt.min.js chưa được tải. Hoạt động ở chế độ Mock Offline.");
      if (onStatusChange) onStatusChange(false, "Thư viện MQTT chưa nạp");
      return;
    }

    try {
      const clientId = `bss_web3d_${Math.random().toString(16).substr(2, 8)}`;
      console.log(`[MQTT] Đang kết nối tới ${brokerUrl} với ID: ${clientId}...`);

      client = mqtt.connect(brokerUrl, {
        clientId: clientId,
        clean: true,
        connectTimeout: 5000,
        reconnectPeriod: 10000,
      });

      client.on("connect", () => {
        isConnected = true;
        console.log(`[MQTT] Đã kết nối thành công tới ${brokerUrl}!`);
        client.subscribe(COMMAND_TOPIC, (err) => {
          if (!err) {
            console.log(`[MQTT] Đã lắng nghe topic điều khiển: ${COMMAND_TOPIC}`);
          }
        });

        if (onStatusChange) onStatusChange(true, "Đã kết nối Cloud EMQX");
      });

      client.on("message", (topic, message) => {
        try {
          const payload = JSON.parse(message.toString());
          console.log(`[MQTT] Nhận lệnh từ ${topic}:`, payload);
          if (commandCallback) {
            commandCallback(payload);
          }
        } catch (e) {
          console.error("[MQTT] Lỗi parse JSON lệnh:", e);
        }
      });

      client.on("error", (err) => {
        console.warn("[MQTT] Lỗi kết nối MQTT:", err.message);
        isConnected = false;
        if (onStatusChange) onStatusChange(false, "Lỗi kết nối");
      });

      client.on("close", () => {
        isConnected = false;
        if (onStatusChange) onStatusChange(false, "Mất kết nối");
      });
    } catch (ex) {
      console.error("[MQTT] Ngoại lệ khi kết nối:", ex);
      isConnected = false;
      if (onStatusChange) onStatusChange(false, ex.message);
    }
  }

  function disconnect(onStatusChange = null) {
    if (client) {
      client.end(true);
      client = null;
    }
    isConnected = false;
    if (onStatusChange) onStatusChange(false, "Chế độ Offline");
  }

  function publishTelemetry(slotsData) {
    const payload = {
      stationId: stationId,
      timestamp: new Date().toISOString(),
      gridVoltage: 380.0,
      totalPowerKW: (slotsData.filter((s) => s.isCharging).length * 1.2).toFixed(2),
      slots: slotsData.map((s) => ({
        slotId: s.id,
        batteryId: s.batteryId,
        hasBattery: s.hasBattery,
        soc: parseFloat(s.soc.toFixed(1)),
        soh: parseFloat(s.soh.toFixed(1)),
        temperature: parseFloat(s.temperature.toFixed(1)),
        voltage: parseFloat(s.voltage.toFixed(1)),
        current: parseFloat(s.current.toFixed(1)),
        isCharging: s.isCharging,
        isDoorOpen: s.isDoorOpen,
        isOverheated: s.isOverheated,
        status: s.isOverheated ? "OVERHEAT_ALARM" : (!s.hasBattery ? "EMPTY" : (s.soc >= 90 ? "READY" : "CHARGING")),
      })),
    };

    if (isConnected && client) {
      client.publish(TELEMETRY_TOPIC, JSON.stringify(payload));
    }
    return payload;
  }

  return {
    init,
    connect,
    disconnect,
    publishTelemetry,
    isConnected: () => isConnected,
  };
})();
