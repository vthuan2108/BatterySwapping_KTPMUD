/**
 * simulatorController.js - V2 (Real-world VinFast Kiosk Swap Flow & IoT Engine)
 * Features:
 * - Unobstructed 3D view with collapsible right sidebar
 * - Step-by-step Interactive Kiosk Swap Flow (QR scan, old battery insert, BMS check, full battery eject)
 * - Realtime synchronized 3D touchscreen texture on the cabinet
 * - Eject / Dock physics animation for batteries
 * - Overheat 3D particle smoke simulation (> 55°C)
 */

const SimulatorController = (function () {
  let scene, camera, renderer, controls;
  let raycaster, mouse;
  let cabinetData = null;

  // Real-time slots state (8 slots matching DB)
  const slotsData = [
    { id: 1, batteryId: "PIN-001", hasBattery: true, isEjected: false, soc: 98.0, soh: 99.0, temperature: 31.0, voltage: 53.8, current: 0.0, isCharging: false, isOverheated: false },
    { id: 2, batteryId: "PIN-002", hasBattery: true, isEjected: false, soc: 65.0, soh: 96.5, temperature: 36.5, voltage: 50.1, current: 12.0, isCharging: true, isOverheated: false },
    { id: 3, batteryId: "PIN-003", hasBattery: true, isEjected: false, soc: 45.0, soh: 95.0, temperature: 38.0, voltage: 48.5, current: 15.0, isCharging: true, isOverheated: false },
    { id: 4, batteryId: "PIN-004", hasBattery: true, isEjected: false, soc: 95.0, soh: 98.5, temperature: 30.5, voltage: 53.2, current: 0.0, isCharging: false, isOverheated: false },
    { id: 5, batteryId: "PIN-005", hasBattery: true, isEjected: false, soc: 22.0, soh: 94.0, temperature: 34.5, voltage: 45.8, current: 15.0, isCharging: true, isOverheated: false },
    { id: 6, batteryId: null, hasBattery: false, isEjected: false, soc: 0.0, soh: 0.0, temperature: 26.0, voltage: 0.0, current: 0.0, isCharging: false, isOverheated: false },
    { id: 7, batteryId: "PIN-007", hasBattery: true, isEjected: false, soc: 88.0, soh: 97.0, temperature: 35.0, voltage: 52.0, current: 8.0, isCharging: true, isOverheated: false },
    { id: 8, batteryId: null, hasBattery: false, isEjected: false, soc: 0.0, soh: 0.0, temperature: 25.5, voltage: 0.0, current: 0.0, isCharging: false, isOverheated: false },
  ];

  let selectedSlotId = 1;
  let autoChargeActive = true;
  let clock;
  let hoveredSlotId = null;

  // Kiosk Swap Flow State Machine
  // STEPS: 'IDLE' -> 'INSERT_OLD' -> 'BMS_EVAL' -> 'EJECT_NEW' -> 'COMPLETED'
  let kioskState = {
    step: "IDLE",
    driverName: "Nguyễn Văn A",
    driverPhone: "0987654321",
    walletBalance: 150000,
    targetEmptySlotId: 6,
    targetFullSlotId: 1,
    oldBatterySoc: 18.0,
    swapFee: 25000,
  };

  // DOM Elements cache
  let dom = {};

  function init() {
    clock = new THREE.Clock();
    raycaster = new THREE.Raycaster();
    mouse = new THREE.Vector2();

    cacheDom();
    setupThreeScene();
    setupLighting();

    // Build 3D Station Model
    cabinetData = CabinetModel.buildCabinet(scene);

    // Init 3D Smoke Particle System
    ParticleSystem.init(scene);

    // Initial visuals sync
    slotsData.forEach((slot) => {
      updateSlotVisuals(slot);
      if (!slot.hasBattery) {
        CabinetModel.setBatteryPresence(slot.id, false, false);
      }
    });

    // Init MQTT sync
    MqttSync.init(handleRemoteCommand);

    setupEventListeners();
    renderKioskFlow();
    renderSidebarMiniGrid();
    updateSidebarSlotDetails();

    // Simulation loop
    setInterval(simulationTick, 1000);

    animate();
    console.log("🚀 BSS 3D VinFast-Style Controller initialized successfully!");
  }

  function cacheDom() {
    dom.container = document.getElementById("canvas-container");
    dom.tooltip = document.getElementById("slot-tooltip");

    // Sidebar
    dom.sidebar = document.getElementById("sidebar-panel");
    dom.btnToggleSidebar = document.getElementById("btn-toggle-sidebar");
    dom.btnCloseSidebar = document.getElementById("btn-close-sidebar");

    // Sidebar slot details
    dom.sideSlotTitle = document.getElementById("side-slot-title");
    dom.sideSoc = document.getElementById("side-soc");
    dom.sideTemp = document.getElementById("side-temp");
    dom.sideCurrent = document.getElementById("side-current");
    dom.sideVoltage = document.getElementById("side-voltage");
    dom.sideSoh = document.getElementById("side-soh");
    dom.sideStatus = document.getElementById("side-status");

    dom.sliderTemp = document.getElementById("slider-temp");
    dom.sliderTempVal = document.getElementById("slider-temp-val");
    dom.sliderSoc = document.getElementById("slider-soc");
    dom.sliderSocVal = document.getElementById("slider-soc-val");

    dom.btnEjectSlot = document.getElementById("btn-eject-slot");
    dom.btnInsertSlot = document.getElementById("btn-insert-slot");
    dom.btnOverheat = document.getElementById("btn-overheat");
    dom.btnReset = document.getElementById("btn-reset");

    // Kiosk Flow Card
    dom.kioskFlowCard = document.getElementById("kiosk-flow-card");
    dom.kioskBadge = document.getElementById("kiosk-badge");
    dom.kioskTitle = document.getElementById("kiosk-title");
    dom.kioskBody = document.getElementById("kiosk-body");
    dom.kioskActions = document.getElementById("kiosk-actions");
  }

  function setupThreeScene() {
    const width = window.innerWidth;
    const height = window.innerHeight;

    scene = new THREE.Scene();
    scene.background = new THREE.Color(0x070c18);
    scene.fog = new THREE.FogExp2(0x070c18, 0.035);

    camera = new THREE.PerspectiveCamera(45, width / height, 0.1, 100);
    // Adjusted camera position to view whole station nicely from top to bottom
    camera.position.set(0, 0.2, 5.0);

    renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: "high-performance" });
    renderer.setSize(width, height);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.15;

    dom.container.appendChild(renderer.domElement);

    controls = new THREE.OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.05;
    controls.maxPolarAngle = Math.PI / 2 + 0.02;
    controls.minDistance = 2.0;
    controls.maxDistance = 11.0;
    controls.target.set(0, 0.1, 0);
  }

  function setupLighting() {
    // Ambient Light
    const ambientLight = new THREE.AmbientLight(0x64748b, 0.95);
    scene.add(ambientLight);

    // Key Studio Light
    const dirLight = new THREE.DirectionalLight(0xffffff, 1.3);
    dirLight.position.set(4, 7, 5.5);
    dirLight.castShadow = true;
    dirLight.shadow.mapSize.width = 2048;
    dirLight.shadow.mapSize.height = 2048;
    dirLight.shadow.camera.near = 0.5;
    dirLight.shadow.camera.far = 20;
    dirLight.shadow.bias = -0.0005;
    const d = 3.8;
    dirLight.shadow.camera.left = -d;
    dirLight.shadow.camera.right = d;
    dirLight.shadow.camera.top = d;
    dirLight.shadow.camera.bottom = -d;
    scene.add(dirLight);

    // Front soft fill
    const frontFill = new THREE.DirectionalLight(0x38bdf8, 0.7);
    frontFill.position.set(0, 1, 5);
    scene.add(frontFill);

    // Cyan Rim Light
    const rimLight = new THREE.DirectionalLight(0x0284c7, 0.6);
    rimLight.position.set(-5, 3, -4);
    scene.add(rimLight);
  }

  function setupEventListeners() {
    window.addEventListener("resize", onWindowResize);
    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("click", onMouseClick);

    // Toggle Sidebar
    dom.btnToggleSidebar.addEventListener("click", () => {
      dom.sidebar.classList.toggle("collapsed");
    });

    dom.btnCloseSidebar.addEventListener("click", () => {
      dom.sidebar.classList.add("collapsed");
    });

    // Sidebar Sliders
    dom.sliderTemp.addEventListener("input", (e) => {
      const val = parseFloat(e.target.value);
      dom.sliderTempVal.innerText = `${val.toFixed(1)}°C`;
      const slot = getSelectedSlot();
      if (slot) {
        slot.temperature = val;
        checkSafetyLimits(slot);
        updateSidebarSlotDetails();
      }
    });

    dom.sliderSoc.addEventListener("input", (e) => {
      const val = parseFloat(e.target.value);
      dom.sliderSocVal.innerText = `${Math.round(val)}%`;
      const slot = getSelectedSlot();
      if (slot && slot.hasBattery) {
        slot.soc = val;
        updateSlotVisuals(slot);
        updateSidebarSlotDetails();
      }
    });

    // Sidebar Slot Actions
    dom.btnEjectSlot.addEventListener("click", () => {
      const slot = getSelectedSlot();
      if (!slot || !slot.hasBattery) return;
      slot.isEjected = !slot.isEjected;
      CabinetModel.setBatteryEjected(slot.id, slot.isEjected, true);
      dom.btnEjectSlot.innerText = slot.isEjected ? "📥 Đẩy Pin Vào Hộc" : "⏏ Mở Khóa Đẩy Pin Ra";
    });

    dom.btnInsertSlot.addEventListener("click", () => {
      const slot = getSelectedSlot();
      if (!slot) return;
      if (slot.hasBattery) {
        // Eject completely
        slot.hasBattery = false;
        slot.batteryId = null;
        slot.soc = 0;
        slot.current = 0;
        slot.isCharging = false;
        CabinetModel.setBatteryPresence(slot.id, false, true);
      } else {
        // Insert new
        slot.hasBattery = true;
        slot.batteryId = `PIN-00${slot.id}`;
        slot.soc = 92.0;
        slot.temperature = 30.0;
        slot.voltage = 52.5;
        CabinetModel.setBatteryPresence(slot.id, true, true);
      }
      updateSlotVisuals(slot);
      updateSidebarSlotDetails();
    });

    dom.btnOverheat.addEventListener("click", () => {
      const slot = getSelectedSlot();
      if (!slot) return;
      triggerOverheatFault(slot.id, 65.0);
    });

    dom.btnReset.addEventListener("click", () => {
      const slot = getSelectedSlot();
      if (!slot) return;
      resetSlotFault(slot.id);
    });

    // Camera preset buttons
    document.querySelectorAll(".btn-preset").forEach((btn) => {
      btn.addEventListener("click", () => {
        document.querySelectorAll(".btn-preset").forEach((b) => b.classList.remove("active"));
        btn.classList.add("active");
        const preset = btn.getAttribute("data-view");
        setCameraPreset(preset);
      });
    });
  }

  function setCameraPreset(preset) {
    if (typeof TWEEN === "undefined") return;

    let targetPos, targetLookAt;
    if (preset === "front") {
      targetPos = { x: 0, y: 0.1, z: 4.8 };
      targetLookAt = { x: 0, y: 0.1, z: 0 };
    } else if (preset === "iso") {
      targetPos = { x: 3.2, y: 1.6, z: 4.2 };
      targetLookAt = { x: 0, y: 0.1, z: 0 };
    } else if (preset === "top") {
      targetPos = { x: 0, y: 5.5, z: 1.0 };
      targetLookAt = { x: 0, y: 0.1, z: 0 };
    } else if (preset === "focus") {
      const slot = getSelectedSlot();
      const slotObj = cabinetData.slots.find((s) => s.slotId === slot.id);
      if (slotObj) {
        targetPos = { x: slotObj.group.position.x * 0.7, y: slotObj.group.position.y + 0.1, z: 2.4 };
        targetLookAt = { x: slotObj.group.position.x, y: slotObj.group.position.y, z: 0.2 };
      }
    }

    if (targetPos) {
      new TWEEN.Tween(camera.position).to(targetPos, 850).easing(TWEEN.Easing.Cubic.Out).start();
      new TWEEN.Tween(controls.target).to(targetLookAt, 850).easing(TWEEN.Easing.Cubic.Out).start();
    }
  }

  function getSelectedSlot() {
    return slotsData.find((s) => s.id === selectedSlotId);
  }

  function selectSlot(slotId) {
    selectedSlotId = slotId;
    updateSidebarSlotDetails();
    updateMiniGridActive();
  }

  // ==============================================================
  // INTERACTIVE KIOSK SWAP WORKFLOW STATE MACHINE
  // ==============================================================
  function renderKioskFlow() {
    const s = kioskState.step;

    if (s === "IDLE") {
      dom.kioskBadge.innerText = "BƯỚC 1 / 4 • SẴN SÀNG";
      dom.kioskTitle.innerText = "CHÀO MỪNG ĐẾN VỚI TRẠM ĐỔI PIN BSS";

      dom.kioskBody.innerHTML = `
        <div class="kiosk-qr-box">
          <svg viewBox="0 0 100 100" fill="#000">
            <rect x="5" y="5" width="30" height="30" fill="#000"/>
            <rect x="10" y="10" width="20" height="20" fill="#fff"/>
            <rect x="14" y="14" width="12" height="12" fill="#000"/>
            <rect x="65" y="5" width="30" height="30" fill="#000"/>
            <rect x="70" y="10" width="20" height="20" fill="#fff"/>
            <rect x="74" y="14" width="12" height="12" fill="#000"/>
            <rect x="5" y="65" width="30" height="30" fill="#000"/>
            <rect x="10" y="70" width="20" height="20" fill="#fff"/>
            <rect x="14" y="74" width="12" height="12" fill="#000"/>
            <circle cx="50" cy="50" r="8" fill="#0284c7"/>
            <rect x="45" y="20" width="8" height="15" fill="#000"/>
            <rect x="45" y="65" width="8" height="15" fill="#000"/>
          </svg>
        </div>
        <div class="kiosk-step-info">
          <div class="kiosk-step-desc">
            Quét mã QR trên ứng dụng di động hoặc bấm nút bên dưới để bắt đầu chu trình đổi pin tự động.
          </div>
          <div class="kiosk-driver-info">
            Tài xế demo: <b>${kioskState.driverName}</b> (${kioskState.driverPhone})<br>
            Số dư ví: <b>${kioskState.walletBalance.toLocaleString("vi-VN")} đ</b>
          </div>
        </div>
      `;

      dom.kioskActions.innerHTML = `
        <button class="btn-kiosk-primary" id="btn-start-swap">
          🚀 BẮT ĐẦU ĐỔI PIN (QUÉT QR)
        </button>
      `;

      document.getElementById("btn-start-swap").addEventListener("click", startSwapProcess);
      CabinetModel.updateKioskScreen("STANDBY", "CHÀO MỪNG ĐẾN VỚI TRẠM ĐỔI PIN", "Quét QR để đổi pin", "#10b981");

    } else if (s === "INSERT_OLD") {
      const emptySlot = kioskState.targetEmptySlotId;
      dom.kioskBadge.innerText = "BƯỚC 2 / 4 • CẮM PIN CŨ";
      dom.kioskTitle.innerText = `VUI LÒNG CẮM PIN CŨ VÀO Ô #${emptySlot}`;

      dom.kioskBody.innerHTML = `
        <div class="kiosk-step-info" style="width: 100%;">
          <div class="kiosk-step-desc">
            Khay <b>Ô #${emptySlot}</b> đã mở chốt khóa (viền LED nhấp nháy xanh dương). Hãy cắm viên pin cạn của bạn vào ô này.
          </div>
          <div class="kiosk-driver-info">
            Pin cũ dự kiến: <b>PIN-CŨ-18%</b> | Trạng thái: Chờ cắm vào hộc tủ.
          </div>
        </div>
      `;

      dom.kioskActions.innerHTML = `
        <button class="btn-kiosk-primary" id="btn-do-insert-old" style="background: linear-gradient(135deg, #0284c7, #2563eb);">
          🔋 [THAO TÁC] CẮM PIN CŨ VÀO Ô #${emptySlot}
        </button>
        <button class="btn-kiosk-secondary" id="btn-cancel-swap">Hủy</button>
      `;

      document.getElementById("btn-do-insert-old").addEventListener("click", onOldBatteryInserted);
      document.getElementById("btn-cancel-swap").addEventListener("click", resetSwapWorkflow);

      // Flash target slot LED
      CabinetModel.setSlotLedColor(emptySlot, 0x38bdf8, 3.0);
      CabinetModel.updateKioskScreen("SWAP", "VUI LÒNG CẮM PIN CŨ VÀO", "Chốt điện từ đã mở", "#38bdf8", emptySlot);

    } else if (s === "BMS_EVAL") {
      dom.kioskBadge.innerText = "BƯỚC 3 / 4 • KIỂM ĐỊNH BMS";
      dom.kioskTitle.innerText = "HỆ THỐNG ĐANG KIỂM ĐỊNH PIN CŨ...";

      dom.kioskBody.innerHTML = `
        <div class="kiosk-step-info" style="width: 100%;">
          <div class="kiosk-step-desc">
            Đang đo dung lượng SoC, điện áp cell, nhiệt độ pack pin và tiến hành trừ phí dịch vụ...
          </div>
          <div class="kiosk-driver-info" style="color: #10b981;">
            ✔ Đã khóa chốt Ô #${kioskState.targetEmptySlotId} an toàn.<br>
            ✔ Dung lượng ghi nhận: <b>${kioskState.oldBatterySoc}%</b> | Nhiệt độ: <b>33.5°C</b><br>
            ✔ Phí đổi pin: <b>${kioskState.swapFee.toLocaleString("vi-VN")} đ</b> (Trừ trực tiếp vào ví)
          </div>
        </div>
      `;

      dom.kioskActions.innerHTML = `
        <div style="font-size: 12px; color: var(--accent-cyan); display: flex; align-items: center; gap: 8px;">
          <span class="status-dot"></span> Đang chuẩn bị nhả viên pin đầy tối ưu nhất...
        </div>
      `;

      CabinetModel.updateKioskScreen("SWAP", "KIỂM ĐỊNH BMS THÀNH CÔNG", "Trừ phí: 25.000 đ", "#10b981", kioskState.targetEmptySlotId);

    } else if (s === "EJECT_NEW") {
      const fullSlot = kioskState.targetFullSlotId;
      dom.kioskBadge.innerText = "BƯỚC 4 / 4 • NHẬN PIN MỚI";
      dom.kioskTitle.innerText = `VUI LÒNG RÚT VIÊN PIN ĐẦY TỪ Ô #${fullSlot}`;

      dom.kioskBody.innerHTML = `
        <div class="kiosk-step-info" style="width: 100%;">
          <div class="kiosk-step-desc">
            Viên pin đạt chuẩn (Dung lượng <b>98%</b>) tại <b>Ô #${fullSlot}</b> đã tự động mở khóa và đẩy nhô ra phía trước. Bạn hãy nắm quai xách và rút ra!
          </div>
          <div class="kiosk-driver-info" style="border-color: var(--accent-lime);">
            Mã pin mới: <b>PIN-001 (98% SoC)</b> | Đèn viền xanh lá rực rỡ.
          </div>
        </div>
      `;

      dom.kioskActions.innerHTML = `
        <button class="btn-kiosk-primary" id="btn-do-take-new" style="background: linear-gradient(135deg, #10b981, #059669);">
          🔋 [THAO TÁC] RÚT PIN ĐẦY KHỎI Ô #${fullSlot}
        </button>
      `;

      document.getElementById("btn-do-take-new").addEventListener("click", onNewBatteryTaken);

      // Eject target battery in 3D & light up lime green
      CabinetModel.setBatteryEjected(fullSlot, true, true);
      CabinetModel.setSlotLedColor(fullSlot, 0x84cc16, 3.2);
      CabinetModel.updateKioskScreen("SWAP", "VUI LÒNG RÚT PIN ĐẦY", "Pin đạt 98% dung lượng", "#84cc16", fullSlot);

    } else if (s === "COMPLETED") {
      dom.kioskBadge.innerText = "HOÀN TẤT • THÀNH CÔNG";
      dom.kioskTitle.innerText = "🎉 ĐỔI PIN THÀNH CÔNG! CHÚC THƯỢNG LỘ BÌNH AN";

      dom.kioskBody.innerHTML = `
        <div class="kiosk-step-info" style="width: 100%;">
          <div class="kiosk-step-desc">
            Giao dịch đã được ghi nhận vào hệ thống. Biên lai điện tử đã gửi tới số điện thoại <b>${kioskState.driverPhone}</b>.
          </div>
          <div class="kiosk-driver-info" style="border-color: #10b981;">
            Số dư ví còn lại: <b>${kioskState.walletBalance.toLocaleString("vi-VN")} đ</b><br>
            Pin mới nhận: <b>PIN-001 (98% SoC)</b>
          </div>
        </div>
      `;

      dom.kioskActions.innerHTML = `
        <button class="btn-kiosk-secondary" id="btn-reset-workflow" style="width: 100%;">
          Quay về màn hình chờ (Tự động sau 6s)
        </button>
      `;

      document.getElementById("btn-reset-workflow").addEventListener("click", resetSwapWorkflow);
      CabinetModel.updateKioskScreen("SWAP", "ĐỔI PIN THÀNH CÔNG!", "Chúc bạn vạn dặm bình an", "#10b981");
    }
  }

  function startSwapProcess() {
    if (kioskState.step !== "IDLE" && kioskState.step !== "STANDBY") return;
    if (kioskState.walletBalance < kioskState.swapFee) { alert("Số dư không đủ."); return; }
    // Find empty slot (e.g. Slot 6 or 8)
    const emptySlot = slotsData.find((s) => !s.hasBattery);
    if (!emptySlot) { alert("Không còn ngăn trống."); return; }
    kioskState.targetEmptySlotId = emptySlot.id;

    // Find best full battery (e.g. Slot 1 or 4)
    const fullSlot = slotsData.find((s) => s.hasBattery && s.soc >= 90 && s.soh >= 80 && s.temperature <= 45 && !s.isOverheated);
    if (!fullSlot) { alert("Không có pin đạt chuẩn."); return; }
    kioskState.targetFullSlotId = fullSlot.id;

    kioskState.step = "INSERT_OLD";
    renderKioskFlow();
  }

  function onOldBatteryInserted() {
    if (kioskState.step !== "INSERT_OLD") return;
    const slotId = kioskState.targetEmptySlotId;
    const slot = slotsData.find((s) => s.id === slotId);

    if (slot) {
      slot.hasBattery = true;
      slot.batteryId = "PIN-CU-06";
      slot.soc = kioskState.oldBatterySoc;
      slot.soh = 96.0;
      slot.temperature = 33.5;
      slot.voltage = 45.0;
      slot.current = 15.0;
      slot.isCharging = true;
      CabinetModel.setBatteryPresence(slotId, true, true);
      updateSlotVisuals(slot);
    }

    // Deduct wallet fee
    kioskState.walletBalance -= kioskState.swapFee;

    kioskState.step = "BMS_EVAL";
    renderKioskFlow();

    // Auto progress to Eject New after 1.8s
    setTimeout(() => {
      if (kioskState.step !== "BMS_EVAL") return;
      kioskState.step = "EJECT_NEW";
      renderKioskFlow();
    }, 1800);
  }

  function onNewBatteryTaken() {
    if (kioskState.step !== "EJECT_NEW") return;
    const fullSlotId = kioskState.targetFullSlotId;
    const slot = slotsData.find((s) => s.id === fullSlotId);

    if (slot) {
      slot.hasBattery = false;
      slot.isEjected = false;
      slot.batteryId = null;
      slot.soc = 0;
      slot.voltage = 0;
      slot.current = 0;
      slot.isCharging = false;
      CabinetModel.setBatteryPresence(fullSlotId, false, true);
      updateSlotVisuals(slot);
    }

    kioskState.step = "COMPLETED";
    renderKioskFlow();
    renderSidebarMiniGrid();

    // Auto reset to standby after 6s
    setTimeout(() => {
      if (kioskState.step === "COMPLETED") {
        resetSwapWorkflow();
      }
    }, 6000);
  }

  function resetSwapWorkflow() {
    kioskState.step = "IDLE";
    renderKioskFlow();
    slotsData.forEach((s) => updateSlotVisuals(s));
  }

  // ==============================================================
  // SIDEBAR & FAULT INJECTION CONTROLS
  // ==============================================================
  function renderSidebarMiniGrid() {
    const grid = document.getElementById("slots-grid-mini");
    if (!grid) return;

    grid.innerHTML = "";
    slotsData.forEach((slot) => {
      const btn = document.createElement("button");
      btn.className = `mini-slot-btn ${slot.id === selectedSlotId ? "active" : ""}`;

      if (slot.isOverheated) {
        btn.classList.add("status-error");
      } else if (!slot.hasBattery) {
        btn.classList.add("status-empty");
      } else if (slot.soc >= 90) {
        btn.classList.add("status-full");
      } else if (slot.isCharging) {
        btn.classList.add("status-charging");
      }

      btn.innerHTML = `
        <span>#${slot.id}</span>
        <span class="slot-soc">${slot.hasBattery ? `${Math.round(slot.soc)}%` : "TRỐNG"}</span>
      `;

      btn.addEventListener("click", () => {
        selectSlot(slot.id);
      });

      grid.appendChild(btn);
    });
  }

  function updateMiniGridActive() {
    document.querySelectorAll(".mini-slot-btn").forEach((btn, idx) => {
      btn.classList.toggle("active", idx + 1 === selectedSlotId);
    });
  }

  function updateSidebarSlotDetails() {
    const slot = getSelectedSlot();
    if (!slot) return;

    dom.sideSlotTitle.innerText = `Khay #${slot.id} - ${slot.hasBattery ? slot.batteryId : "Ngăn trống"}`;

    if (slot.isOverheated) {
      dom.sideStatus.innerText = "🔥 QUÁ NHIỆT";
      dom.sideStatus.style.color = "#ef4444";
    } else if (!slot.hasBattery) {
      dom.sideStatus.innerText = "TRỐNG";
      dom.sideStatus.style.color = "#94a3b8";
    } else if (slot.soc >= 90) {
      dom.sideStatus.innerText = "ĐẦY (SẴN SÀNG)";
      dom.sideStatus.style.color = "#84cc16";
    } else if (slot.isCharging) {
      dom.sideStatus.innerText = "ĐANG SẠC";
      dom.sideStatus.style.color = "#f59e0b";
    } else {
      dom.sideStatus.innerText = "CHỜ SẠC";
      dom.sideStatus.style.color = "#38bdf8";
    }

    dom.sideSoc.innerText = slot.hasBattery ? `${Math.round(slot.soc)}%` : "--";
    dom.sideSoc.className = `val ${slot.soc >= 90 ? "green" : slot.soc < 30 ? "red" : "amber"}`;

    dom.sideTemp.innerText = `${slot.temperature.toFixed(1)}°C`;
    dom.sideTemp.className = `val ${slot.temperature >= 55 ? "red" : slot.temperature >= 40 ? "amber" : "green"}`;

    dom.sideCurrent.innerText = `${slot.current.toFixed(1)} A`;
    dom.sideVoltage.innerText = `${slot.voltage.toFixed(1)} V`;
    dom.sideSoh.innerText = slot.hasBattery ? `${slot.soh.toFixed(1)}%` : "--";

    dom.sliderTemp.value = slot.temperature;
    dom.sliderTempVal.innerText = `${slot.temperature.toFixed(1)}°C`;

    dom.sliderSoc.value = slot.soc;
    dom.sliderSocVal.innerText = `${Math.round(slot.soc)}%`;
    dom.sliderSoc.disabled = !slot.hasBattery;

    dom.btnEjectSlot.innerText = slot.isEjected ? "📥 Đẩy Pin Vào Hộc" : "⏏ Mở Khóa Đẩy Pin Ra";
    dom.btnInsertSlot.innerText = slot.hasBattery ? "❌ Gỡ Pin Khỏi Hộc" : "➕ Cắm Thêm Pin Mới";
  }

  function triggerOverheatFault(slotId, targetTemp = 65.0) {
    const slot = slotsData.find((s) => s.id === slotId);
    if (!slot) return;

    slot.temperature = targetTemp;
    checkSafetyLimits(slot);
    updateSidebarSlotDetails();
    renderSidebarMiniGrid();
  }

  function resetSlotFault(slotId) {
    const slot = slotsData.find((s) => s.id === slotId);
    if (!slot) return;

    slot.temperature = 31.0;
    slot.isOverheated = false;
    ParticleSystem.stopOverheat(slotId);
    CabinetModel.setAlarmLight(slotId, false);

    if (slot.hasBattery && slot.soc < 100) {
      slot.isCharging = true;
      slot.current = 12.0;
    }

    updateSlotVisuals(slot);
    updateSidebarSlotDetails();
    renderSidebarMiniGrid();
  }

  function checkSafetyLimits(slot) {
    if (slot.temperature >= 55.0) {
      if (!slot.isOverheated) {
        slot.isOverheated = true;
        slot.isCharging = false;
        slot.current = 0.0;
        slot.isEjected = false;
        CabinetModel.setBatteryEjected(slot.id, false, true);

        // Trigger 3D smoke & alarm light
        const slotObj = cabinetData.slots.find((s) => s.slotId === slot.id);
        if (slotObj) {
          ParticleSystem.startOverheat(slot.id, slotObj.smokeAnchor);
          CabinetModel.setAlarmLight(slot.id, true);
        }

        CabinetModel.updateKioskScreen("ALARM", `SỰ CỐ QUÁ NHIỆT Ô #${slot.id}`, "ĐÃ NGẮT SẠC & KHÓA CHỐT AN TOÀN", "#ef4444", slot.id);
      }
    } else {
      if (slot.isOverheated && slot.temperature < 50.0) {
        slot.isOverheated = false;
        ParticleSystem.stopOverheat(slot.id);
        CabinetModel.setAlarmLight(slot.id, false);
      }
    }
    updateSlotVisuals(slot);
  }

  function updateSlotVisuals(slot) {
    if (slot.isOverheated) {
      CabinetModel.setSlotLedColor(slot.id, 0xef4444, 2.8); // Flashing Red
    } else if (!slot.hasBattery) {
      CabinetModel.setSlotLedColor(slot.id, 0x64748b, 0.7); // Gray Empty
    } else if (slot.soc >= 90.0) {
      CabinetModel.setSlotLedColor(slot.id, 0x84cc16, 2.2); // Lime Green Ready
    } else if (slot.isCharging) {
      CabinetModel.setSlotLedColor(slot.id, 0xf59e0b, 1.8); // Amber Charging
    } else {
      CabinetModel.setSlotLedColor(slot.id, 0x0284c7, 1.4); // Blue Standby
    }

    if (slot.hasBattery) {
      CabinetModel.updateBatteryDisplay(slot.id, slot.soc, slot.isCharging);
    }
  }

  function simulationTick() {
    if (!autoChargeActive) return;

    slotsData.forEach((slot) => {
      if (slot.hasBattery && slot.isCharging && !slot.isOverheated) {
        slot.soc = Math.min(100, slot.soc + 0.35);

        if (slot.soc >= 98.0) {
          slot.current = 2.0;
        } else if (slot.soc >= 80.0) {
          slot.current = 8.0;
        } else {
          slot.current = 15.0;
        }

        if (slot.soc >= 100.0) {
          slot.isCharging = false;
          slot.current = 0.0;
          updateSlotVisuals(slot);
        }

        updateSlotVisuals(slot);
      }
    });

    updateSidebarSlotDetails();
    renderSidebarMiniGrid();
    MqttSync.publishTelemetry(slotsData);
  }

  function handleRemoteCommand(cmd) {
    if (!cmd || !cmd.command) return;
    const slotId = cmd.slotId || 1;

    switch (cmd.command) {
      case "START_SWAP":
        startSwapProcess();
        break;
      case "TRIGGER_OVERHEAT":
        triggerOverheatFault(slotId, cmd.temperature || 65.0);
        break;
      case "RESET_ALARM":
        resetSlotFault(slotId);
        break;
    }
  }

  function onMouseMove(event) {
    mouse.x = (event.clientX / window.innerWidth) * 2 - 1;
    mouse.y = -(event.clientY / window.innerHeight) * 2 + 1;

    raycaster.setFromCamera(mouse, camera);
    const intersects = raycaster.intersectObjects(scene.children, true);

    let foundSlotId = null;
    for (let i = 0; i < intersects.length; i++) {
      let obj = intersects[i].object;
      while (obj) {
        if (obj.userData && obj.userData.slotId) {
          foundSlotId = obj.userData.slotId;
          break;
        }
        obj = obj.parent;
      }
      if (foundSlotId) break;
    }

    if (foundSlotId) {
      hoveredSlotId = foundSlotId;
      showTooltip(event.clientX, event.clientY, foundSlotId);
      document.body.style.cursor = "pointer";
    } else {
      hoveredSlotId = null;
      hideTooltip();
      document.body.style.cursor = "default";
    }
  }

  function onMouseClick(event) {
    if (event.clientX > window.innerWidth - 370 && !dom.sidebar.classList.contains("collapsed")) {
      return; // Clicking inside sidebar
    }
    if (event.clientY > window.innerHeight - 240 && event.clientX < 460) {
      return; // Clicking inside kiosk card
    }

    if (hoveredSlotId) {
      selectSlot(hoveredSlotId);

      // If in swap workflow and clicked target slot
      if (kioskState.step === "INSERT_OLD" && hoveredSlotId === kioskState.targetEmptySlotId) {
        onOldBatteryInserted();
      } else if (kioskState.step === "EJECT_NEW" && hoveredSlotId === kioskState.targetFullSlotId) {
        onNewBatteryTaken();
      }
    }
  }

  function showTooltip(x, y, slotId) {
    const slot = slotsData.find((s) => s.id === slotId);
    if (!slot) return;

    dom.tooltip.style.display = "block";
    dom.tooltip.style.left = `${Math.min(x + 16, window.innerWidth - 230)}px`;
    dom.tooltip.style.top = `${Math.min(y + 16, window.innerHeight - 190)}px`;

    const statusBadge = slot.isOverheated
      ? "<span style='color:#ef4444'>🔥 QUÁ NHIỆT</span>"
      : !slot.hasBattery
      ? "<span style='color:#64748b'>TRỐNG</span>"
      : slot.soc >= 90
      ? "<span style='color:#84cc16'>ĐẦY PIN</span>"
      : "<span style='color:#f59e0b'>ĐANG SẠC</span>";

    dom.tooltip.innerHTML = `
      <div class="tooltip-title">
        <span>Khay #${slot.id}</span>
        <span>${statusBadge}</span>
      </div>
      <div class="tooltip-row"><span>Mã pin:</span><span class="val">${slot.hasBattery ? slot.batteryId : "--"}</span></div>
      <div class="tooltip-row"><span>Dung lượng:</span><span class="val">${slot.hasBattery ? `${slot.soc.toFixed(1)}%` : "--"}</span></div>
      <div class="tooltip-row"><span>Nhiệt độ:</span><span class="val" style="color:${slot.temperature >= 55 ? "#ef4444" : "#f8fafc"}">${slot.temperature.toFixed(1)}°C</span></div>
      <div class="tooltip-row"><span>Dòng sạc:</span><span class="val">${slot.current.toFixed(1)} A</span></div>
    `;
  }

  function hideTooltip() {
    dom.tooltip.style.display = "none";
  }

  function onWindowResize() {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
  }

  function animate() {
    requestAnimationFrame(animate);

    const delta = clock.getDelta();

    if (typeof TWEEN !== "undefined") {
      TWEEN.update();
    }

    ParticleSystem.update(delta);
    controls.update();

    renderer.render(scene, camera);
  }

  return {
    init,
    selectSlot,
    startSwapProcess,
    triggerOverheatFault,
    resetSlotFault,
  };
})();
