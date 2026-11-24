/**
 * cabinetModel.js - V3 (Multi-Station 3D Architecture)
 * Supports multiple BSS Battery Swapping Station Cabinets in a unified 3D scene:
 * - Station 1: TRAM-BK01 (ĐH Bách Khoa Hà Nội) at x = -2.2
 * - Station 2: TRAM-CG02 (Cầu Giấy) at x = +2.2
 *
 * Each station features:
 * - Custom metallic enclosure with VinFast / modern EV aesthetics
 * - Backlit illuminated overhead signboard with station ID & name
 * - Integrated Security Camera at top-right
 * - Center Brand Emblem (V / BSS)
 * - Independent embedded Touchscreen Kiosk Display with real-time dynamic texture
 * - 8 Receptacle Slots with illuminated LED perimeter rings
 * - Highly detailed front-facing Modular Battery Packs with horizontal ergonomic handles
 * - OLED mini displays on each battery pack
 * - Eject / Slide-out physical animations
 * - Smoke & alarm anchor points per slot
 */

const CabinetModel = (function () {
  let materials = {};
  const stations = {}; // keyed by stationId
  let floorInitialized = false;

  const DEFAULT_CONFIGS = [
    {
      id: "TRAM-BK01",
      name: "Trạm ĐH Bách Khoa Hà Nội",
      shortName: "TRAM-BK01 • ĐH BÁCH KHOA",
      subName: "Cổng Parabol Giải Phóng, Hai Bà Trưng",
      posX: -1.55,
      bodyColor: 0x006ec7, // VinFast Electric Blue Metallic
      accentColor: "#38bdf8",
      neonColor: 0x0284c7,
      ledColor: 0x84cc16,
    },
    {
      id: "TRAM-CG02",
      name: "Trạm Đổi Pin Cầu Giấy",
      shortName: "TRAM-CG02 • CẦU GIẤY",
      subName: "144 Xuân Thủy, Cầu Giấy, Hà Nội",
      posX: 1.55,
      bodyColor: 0x0284c7, // Sleek Modern Cyan Metallic
      accentColor: "#10b981",
      neonColor: 0x10b981,
      ledColor: 0x10b981,
    },
  ];

  function initMaterials() {
    // Dark trim / Accent
    materials.cabinetDark = new THREE.MeshStandardMaterial({
      color: 0x0a1120,
      metalness: 0.8,
      roughness: 0.4,
    });

    materials.whiteStripe = new THREE.MeshStandardMaterial({
      color: 0xffffff,
      roughness: 0.2,
      metalness: 0.1,
    });

    materials.slotCavity = new THREE.MeshStandardMaterial({
      color: 0x070b14,
      metalness: 0.7,
      roughness: 0.8,
    });

    // Battery Materials matching real pack
    materials.batteryBlue = new THREE.MeshStandardMaterial({
      color: 0x0058a3,
      metalness: 0.7,
      roughness: 0.35,
    });

    materials.batteryHandle = new THREE.MeshStandardMaterial({
      color: 0x004580,
      metalness: 0.8,
      roughness: 0.25,
    });

    materials.batteryTrim = new THREE.MeshStandardMaterial({
      color: 0x1e293b,
      metalness: 0.9,
      roughness: 0.2,
    });

    materials.cameraSilver = new THREE.MeshStandardMaterial({
      color: 0xe2e8f0,
      metalness: 0.95,
      roughness: 0.15,
    });

    materials.cameraLens = new THREE.MeshStandardMaterial({
      color: 0x020617,
      metalness: 0.9,
      roughness: 0.05,
    });

    materials.goldPin = new THREE.MeshStandardMaterial({
      color: 0xf59e0b,
      metalness: 0.95,
      roughness: 0.1,
    });
  }

  function initSceneGround(scene) {
    if (floorInitialized) return;
    floorInitialized = true;

    // Wide floor accommodating both stations
    const floorGeo = new THREE.PlaneGeometry(28, 20);
    const floorMat = new THREE.MeshStandardMaterial({
      color: 0x0f172a,
      roughness: 0.85,
      metalness: 0.15,
    });
    const floorMesh = new THREE.Mesh(floorGeo, floorMat);
    floorMesh.rotation.x = -Math.PI / 2;
    floorMesh.position.y = -1.88;
    floorMesh.receiveShadow = true;
    scene.add(floorMesh);

    const gridHelper = new THREE.GridHelper(26, 52, 0x0284c7, 0x1e293b);
    gridHelper.position.y = -1.87;
    scene.add(gridHelper);
  }

  // Draw Station Backlit Neon Billboard atop cabinet
  function createStationSignboard(cfg) {
    const signGroup = new THREE.Group();
    signGroup.position.set(0, 2.38, 0.05);

    // Sign box frame
    const boxGeo = new THREE.BoxGeometry(2.1, 0.36, 0.08);
    const boxMesh = new THREE.Mesh(boxGeo, materials.cabinetDark);
    signGroup.add(boxMesh);

    // Canvas texture for glowing sign
    const signCanvas = document.createElement("canvas");
    signCanvas.width = 512;
    signCanvas.height = 96;
    const sCtx = signCanvas.getContext("2d");

    // Background with glowing border
    sCtx.fillStyle = "#030712";
    sCtx.fillRect(0, 0, 512, 96);
    sCtx.strokeStyle = cfg.accentColor;
    sCtx.lineWidth = 4;
    sCtx.strokeRect(4, 4, 504, 88);

    // Header badge
    sCtx.fillStyle = cfg.accentColor;
    sCtx.font = "bold 16px sans-serif";
    sCtx.textAlign = "center";
    sCtx.fillText("⚡ VINFAST / BSS SMART NETWORK", 256, 26);

    // Main Station Name
    sCtx.fillStyle = "#ffffff";
    sCtx.font = "bold 32px sans-serif";
    sCtx.fillText(cfg.shortName, 256, 64);

    // Subtitle
    sCtx.fillStyle = "#94a3b8";
    sCtx.font = "14px sans-serif";
    sCtx.fillText(cfg.subName, 256, 85);

    const signTex = new THREE.CanvasTexture(signCanvas);
    const signMat = new THREE.MeshStandardMaterial({
      map: signTex,
      emissive: new THREE.Color(cfg.neonColor),
      emissiveMap: signTex,
      emissiveIntensity: 0.65,
      roughness: 0.2,
      metalness: 0.1,
    });

    const signFace = new THREE.Mesh(new THREE.PlaneGeometry(2.04, 0.32), signMat);
    signFace.position.set(0, 0, 0.042);
    signGroup.add(signFace);

    // Top Neon Glow Light Strip
    const neonLight = new THREE.PointLight(cfg.neonColor, 1.2, 4.0);
    neonLight.position.set(0, 0.1, 0.25);
    signGroup.add(neonLight);

    return signGroup;
  }

  // Realtime Kiosk Screen for each station
  function createStationKioskScreen(stationData, cfg) {
    const canvas = document.createElement("canvas");
    canvas.width = 512;
    canvas.height = 340;
    const ctx = canvas.getContext("2d");
    const texture = new THREE.CanvasTexture(canvas);
    texture.needsUpdate = true;

    stationData.kioskCanvas = canvas;
    stationData.kioskCtx = ctx;
    stationData.kioskTexture = texture;

    updateKioskScreen(
      cfg.id,
      "STANDBY",
      cfg.shortName,
      "Bấm 'Bắt đầu đổi pin' hoặc Quét QR",
      cfg.accentColor
    );

    const screenFrameGeo = new THREE.BoxGeometry(0.88, 0.62, 0.03);
    const screenFrame = new THREE.Mesh(screenFrameGeo, materials.cabinetDark);
    screenFrame.position.set(0, 0.95, 1.15 / 2 + 0.015);

    const screenMat = new THREE.MeshBasicMaterial({ map: texture });
    const screenGeo = new THREE.PlaneGeometry(0.82, 0.54);
    const screenMesh = new THREE.Mesh(screenGeo, screenMat);
    screenMesh.position.set(0, 0, 0.018);
    screenFrame.add(screenMesh);

    screenFrame.userData = { stationId: cfg.id, isKiosk: true };
    screenMesh.userData = { stationId: cfg.id, isKiosk: true };

    stationData.kioskScreenMesh = screenMesh;
    return screenFrame;
  }

  function updateKioskScreen(stationId, stepCode, mainText, subText, accentColor = "#38bdf8", slotNumber = null) {
    const st = stations[stationId];
    if (!st || !st.kioskCtx) return;
    const ctx = st.kioskCtx;
    const canvas = st.kioskCanvas;

    // Dark high-tech background
    ctx.fillStyle = "#050b17";
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    // Top Header Banner
    ctx.fillStyle = "#0c1527";
    ctx.fillRect(0, 0, canvas.width, 48);

    // Accent Line
    ctx.fillStyle = accentColor;
    ctx.fillRect(0, 48, canvas.width, 3);

    // Station Name in header
    ctx.fillStyle = "#38bdf8";
    ctx.font = "bold 16px sans-serif";
    ctx.textAlign = "left";
    ctx.fillText("⚡ " + (st.name || stationId), 16, 31);

    // Step Status Tag
    ctx.fillStyle = accentColor;
    ctx.fillRect(canvas.width - 130, 10, 115, 28);
    ctx.fillStyle = "#050b17";
    ctx.font = "bold 13px sans-serif";
    ctx.textAlign = "center";
    ctx.fillText(stepCode, canvas.width - 72, 29);

    if (slotNumber) {
      // Big highlighted slot circle
      ctx.fillStyle = accentColor;
      ctx.beginPath();
      ctx.arc(canvas.width / 2, 135, 52, 0, Math.PI * 2);
      ctx.fill();

      ctx.fillStyle = "#050b17";
      ctx.font = "bold 44px sans-serif";
      ctx.textAlign = "center";
      ctx.fillText(`${slotNumber}`, canvas.width / 2, 150);

      ctx.fillStyle = "#ffffff";
      ctx.font = "bold 20px sans-serif";
      ctx.fillText(mainText, canvas.width / 2, 225);

      ctx.fillStyle = "#94a3b8";
      ctx.font = "16px sans-serif";
      ctx.fillText(subText, canvas.width / 2, 260);
    } else {
      // Mock QR code on left side for standby
      drawMockQrCode(ctx, 30, 72, 150);
      ctx.fillStyle = "#94a3b8";
      ctx.font = "bold 13px sans-serif";
      ctx.textAlign = "center";
      ctx.fillText("QUÉT MÃ QR APP", 30 + 75, 246);

      // Main Instruction Text on right
      ctx.fillStyle = "#ffffff";
      ctx.font = "bold 20px sans-serif";
      ctx.textAlign = "left";
      ctx.fillText(mainText, 205, 105);

      ctx.fillStyle = "#94a3b8";
      ctx.font = "14px sans-serif";
      ctx.fillText("Nhấp chuột vào màn hình để mở Kiosk", 205, 135);

      // Interactive button on 3D kiosk screen
      ctx.fillStyle = accentColor;
      if (ctx.roundRect) {
        ctx.beginPath();
        ctx.roundRect(205, 160, 280, 52, 10);
        ctx.fill();
      } else {
        ctx.fillRect(205, 160, 280, 52);
      }

      ctx.fillStyle = "#050b17";
      ctx.font = "bold 16px sans-serif";
      ctx.textAlign = "center";
      ctx.fillText("👆 CHẠM ĐỂ ĐỔI PIN", 205 + 140, 192);

      ctx.fillStyle = "#38bdf8";
      ctx.font = "13px sans-serif";
      ctx.textAlign = "left";
      ctx.fillText("⚡ Tự động mở hộc & kiểm định BMS", 205, 246);
    }

    if (st.kioskTexture) {
      st.kioskTexture.needsUpdate = true;
    }
  }

  function drawMockQrCode(ctx, x, y, size) {
    ctx.fillStyle = "#ffffff";
    ctx.fillRect(x, y, size, size);

    ctx.fillStyle = "#050b17";
    drawFinder(ctx, x + 8, y + 8, 36);
    drawFinder(ctx, x + size - 44, y + 8, 36);
    drawFinder(ctx, x + 8, y + size - 44, 36);

    for (let r = 0; r < 9; r++) {
      for (let c = 0; c < 9; c++) {
        if (Math.sin(r * 4.2 + c * 2.8) > 0.1) {
          ctx.fillRect(x + 50 + c * 8, y + 50 + r * 8, 6, 6);
        }
      }
    }
  }

  function drawFinder(ctx, fx, fy, fsize) {
    ctx.fillStyle = "#000";
    ctx.fillRect(fx, fy, fsize, fsize);
    ctx.fillStyle = "#fff";
    ctx.fillRect(fx + 6, fy + 6, fsize - 12, fsize - 12);
    ctx.fillStyle = "#000";
    ctx.fillRect(fx + 12, fy + 12, fsize - 24, fsize - 24);
  }

  // Build a single station cabinet at specific posX
  function buildStationCabinet(scene, cfg) {
    initMaterials();
    initSceneGround(scene);

    const stationGroup = new THREE.Group();
    stationGroup.name = `Station_${cfg.id}`;
    stationGroup.position.set(cfg.posX, 0, 0);

    const stationData = {
      id: cfg.id,
      name: cfg.name,
      shortName: cfg.shortName,
      posX: cfg.posX,
      group: stationGroup,
      slots: [],
    };
    stations[cfg.id] = stationData;

    // Overall Tower Dimension (Sleek VinFast Proportions)
    const towerW = 2.2;
    const towerH = 4.2;
    const towerD = 1.15;

    // Cabinet body material with station-specific color
    const cabinetBodyMat = new THREE.MeshStandardMaterial({
      color: cfg.bodyColor,
      metalness: 0.65,
      roughness: 0.3,
    });

    // 1. Base plinth
    const plinthGeo = new THREE.BoxGeometry(towerW + 0.15, 0.25, towerD + 0.15);
    const plinthMesh = new THREE.Mesh(plinthGeo, materials.cabinetDark);
    plinthMesh.position.y = -1.75;
    plinthMesh.receiveShadow = true;
    stationGroup.add(plinthMesh);

    // 2. Main Tower Enclosure
    const towerGeo = new THREE.BoxGeometry(towerW, towerH - 0.25, towerD);
    const towerMesh = new THREE.Mesh(towerGeo, cabinetBodyMat);
    towerMesh.position.y = 0.2;
    towerMesh.castShadow = true;
    towerMesh.receiveShadow = true;
    stationGroup.add(towerMesh);

    // 3. Top Canopy Cap
    const roofGeo = new THREE.BoxGeometry(towerW + 0.05, 0.2, towerD + 0.05);
    const roofMesh = new THREE.Mesh(roofGeo, cabinetBodyMat);
    roofMesh.position.y = towerH / 2 + 0.15;
    stationGroup.add(roofMesh);

    // 4. Backlit Overhead Signboard on Roof
    const signboard = createStationSignboard(cfg);
    stationGroup.add(signboard);

    // 5. White Accent Frame Line on front face
    const frontZ = towerD / 2 + 0.005;
    const stripeMat = materials.whiteStripe;
    const stripeThick = 0.035;

    const topStripe = new THREE.Mesh(new THREE.BoxGeometry(towerW - 0.2, stripeThick, 0.01), stripeMat);
    topStripe.position.set(0, 1.9, frontZ);
    const btmStripe = new THREE.Mesh(new THREE.BoxGeometry(towerW - 0.2, stripeThick, 0.01), stripeMat);
    btmStripe.position.set(0, -1.5, frontZ);
    const leftStripe = new THREE.Mesh(new THREE.BoxGeometry(stripeThick, 3.4, 0.01), stripeMat);
    leftStripe.position.set(-(towerW - 0.2) / 2, 0.2, frontZ);
    const rightStripe = new THREE.Mesh(new THREE.BoxGeometry(stripeThick, 3.4, 0.01), stripeMat);
    rightStripe.position.set((towerW - 0.2) / 2, 0.2, frontZ);

    stationGroup.add(topStripe);
    stationGroup.add(btmStripe);
    stationGroup.add(leftStripe);
    stationGroup.add(rightStripe);

    // 6. Brand Logo V at Top Center
    const logoGroup = new THREE.Group();
    logoGroup.position.set(0, 1.55, frontZ + 0.01);
    const shieldGeo = new THREE.BoxGeometry(0.38, 0.32, 0.02);
    const shieldMesh = new THREE.Mesh(shieldGeo, materials.cabinetDark);
    logoGroup.add(shieldMesh);

    const wingGeo = new THREE.CylinderGeometry(0.015, 0.015, 0.22, 16);
    const leftWing = new THREE.Mesh(wingGeo, materials.cameraSilver);
    leftWing.position.set(-0.06, 0.02, 0.02);
    leftWing.rotation.z = -Math.PI / 5;
    const rightWing = new THREE.Mesh(wingGeo, materials.cameraSilver);
    rightWing.position.set(0.06, 0.02, 0.02);
    rightWing.rotation.z = Math.PI / 5;
    logoGroup.add(leftWing);
    logoGroup.add(rightWing);
    stationGroup.add(logoGroup);

    // 7. Security Camera at Top Right
    const camGroup = new THREE.Group();
    camGroup.position.set(0.72, 1.55, frontZ + 0.01);
    const camRingGeo = new THREE.CylinderGeometry(0.1, 0.1, 0.04, 32);
    camRingGeo.rotateX(Math.PI / 2);
    const camRing = new THREE.Mesh(camRingGeo, materials.cameraSilver);
    const camLensGeo = new THREE.SphereGeometry(0.06, 24, 16, 0, Math.PI * 2, 0, Math.PI / 2);
    camLensGeo.rotateX(Math.PI / 2);
    const camLens = new THREE.Mesh(camLensGeo, materials.cameraLens);
    camLens.position.z = 0.02;
    camGroup.add(camRing);
    camGroup.add(camLens);
    stationGroup.add(camGroup);

    // 8. Embedded Touchscreen Kiosk Display
    const screenFrame = createStationKioskScreen(stationData, cfg);
    stationGroup.add(screenFrame);

    // 9. Brand Name Text near bottom
    const brandCanvas = document.createElement("canvas");
    brandCanvas.width = 512;
    brandCanvas.height = 100;
    const bCtx = brandCanvas.getContext("2d");
    bCtx.fillStyle = "#ffffff";
    bCtx.font = "bold 44px sans-serif";
    bCtx.textAlign = "center";
    bCtx.fillText(`BSS • ${cfg.id}`, 256, 65);
    const brandTex = new THREE.CanvasTexture(brandCanvas);
    const brandMat = new THREE.MeshBasicMaterial({ map: brandTex, transparent: true });
    const brandMesh = new THREE.Mesh(new THREE.PlaneGeometry(1.2, 0.24), brandMat);
    brandMesh.position.set(0, -1.35, frontZ + 0.01);
    stationGroup.add(brandMesh);

    // 10. 8 Receptacle Battery Slots (2 Columns x 4 Rows)
    const colX = [-0.52, 0.52];
    const rowY = [0.35, -0.15, -0.65, -1.15];
    const slotW = 0.72;
    const slotH = 0.40;
    const slotD = 0.85;

    let slotCounter = 1;
    for (let r = 0; r < 4; r++) {
      for (let c = 0; c < 2; c++) {
        const slotId = slotCounter++;
        const posX = colX[c];
        const posY = rowY[r];
        const posZ = frontZ;

        const slotGroup = new THREE.Group();
        slotGroup.position.set(posX, posY, posZ);
        slotGroup.name = `${cfg.id}_Slot_${slotId}`;

        // Slot Receptacle Bezel
        const bezelGeo = new THREE.BoxGeometry(slotW + 0.04, slotH + 0.04, 0.04);
        const bezelMesh = new THREE.Mesh(bezelGeo, materials.cabinetDark);
        bezelMesh.position.z = -0.01;
        slotGroup.add(bezelMesh);

        // Slot Inner Cavity
        const cavGeo = new THREE.BoxGeometry(slotW - 0.04, slotH - 0.04, slotD);
        const cavMesh = new THREE.Mesh(cavGeo, materials.slotCavity);
        cavMesh.position.z = -slotD / 2;
        slotGroup.add(cavMesh);

        // Gold Charging Terminals
        const pin1 = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.015, 0.06, 16), materials.goldPin);
        pin1.rotation.x = Math.PI / 2;
        pin1.position.set(-0.12, -0.08, -slotD + 0.03);
        const pin2 = pin1.clone();
        pin2.position.x = 0.12;
        slotGroup.add(pin1);
        slotGroup.add(pin2);

        // LED Perimeter Ring
        const ledRingMat = new THREE.MeshStandardMaterial({
          color: cfg.ledColor,
          emissive: cfg.ledColor,
          emissiveIntensity: 2.2,
          roughness: 0.1,
        });

        const ringGroup = new THREE.Group();
        ringGroup.position.z = 0.02;
        const rw = slotW - 0.02;
        const rh = slotH - 0.02;
        const rt = 0.022;

        const rTop = new THREE.Mesh(new THREE.BoxGeometry(rw, rt, 0.02), ledRingMat);
        rTop.position.y = rh / 2;
        const rBtm = new THREE.Mesh(new THREE.BoxGeometry(rw, rt, 0.02), ledRingMat);
        rBtm.position.y = -rh / 2;
        const rLeft = new THREE.Mesh(new THREE.BoxGeometry(rt, rh, 0.02), ledRingMat);
        rLeft.position.x = -rw / 2;
        const rRight = new THREE.Mesh(new THREE.BoxGeometry(rt, rh, 0.02), ledRingMat);
        rRight.position.x = rw / 2;

        ringGroup.add(rTop);
        ringGroup.add(rBtm);
        ringGroup.add(rLeft);
        ringGroup.add(rRight);
        slotGroup.add(ringGroup);

        // Internal PointLight
        const slotLight = new THREE.PointLight(cfg.ledColor, 1.2, 1.2);
        slotLight.position.set(0, 0, -0.2);
        slotGroup.add(slotLight);

        // Overheat Alarm Spotlight
        const alarmLight = new THREE.PointLight(0xef4444, 0, 2.5);
        alarmLight.position.set(0, 0, 0.4);
        slotGroup.add(alarmLight);

        // Battery Pack 3D
        const batteryGroup = new THREE.Group();
        batteryGroup.position.set(0, 0, 0);
        batteryGroup.name = `${cfg.id}_Battery_${slotId}`;

        const batW = slotW - 0.08;
        const batH = slotH - 0.08;
        const batD = slotD - 0.05;

        const batBodyGeo = new THREE.BoxGeometry(batW, batH, batD);
        const batBodyMesh = new THREE.Mesh(batBodyGeo, materials.batteryBlue);
        batBodyMesh.position.z = -batD / 2 + 0.02;
        batteryGroup.add(batBodyMesh);

        const faceGeo = new THREE.BoxGeometry(batW + 0.01, batH + 0.01, 0.04);
        const faceMesh = new THREE.Mesh(faceGeo, materials.batteryBlue);
        faceMesh.position.z = 0.02;
        batteryGroup.add(faceMesh);

        const upperIndent = new THREE.Mesh(new THREE.BoxGeometry(batW * 0.75, 0.06, 0.03), materials.batteryHandle);
        upperIndent.position.set(0, 0.08, 0.035);
        const lowerIndent = new THREE.Mesh(new THREE.BoxGeometry(batW * 0.75, 0.06, 0.03), materials.batteryHandle);
        lowerIndent.position.set(0, -0.08, 0.035);
        batteryGroup.add(upperIndent);
        batteryGroup.add(lowerIndent);

        const handleBarGeo = new THREE.BoxGeometry(batW * 0.68, 0.055, 0.055);
        const handleBar = new THREE.Mesh(handleBarGeo, materials.batteryHandle);
        handleBar.position.set(0, 0, 0.055);
        batteryGroup.add(handleBar);

        // Battery Mini OLED Display
        const oledCanvas = document.createElement("canvas");
        oledCanvas.width = 128;
        oledCanvas.height = 64;
        const oCtx = oledCanvas.getContext("2d");
        oCtx.fillStyle = "#000";
        oCtx.fillRect(0, 0, 128, 64);
        oCtx.fillStyle = "#10b981";
        oCtx.font = "bold 26px monospace";
        oCtx.textAlign = "center";
        oCtx.fillText("95%", 64, 34);
        oCtx.font = "bold 14px sans-serif";
        oCtx.fillText("READY", 64, 54);
        const oledTex = new THREE.CanvasTexture(oledCanvas);
        const oledMat = new THREE.MeshBasicMaterial({ map: oledTex });
        const oledMesh = new THREE.Mesh(new THREE.PlaneGeometry(0.12, 0.05), oledMat);
        oledMesh.position.set(0, 0, 0.085);
        batteryGroup.add(oledMesh);

        // Tag user data for raycaster interaction
        faceMesh.userData = { stationId: cfg.id, slotId: slotId, type: "battery" };
        handleBar.userData = { stationId: cfg.id, slotId: slotId, type: "handle" };
        cavMesh.userData = { stationId: cfg.id, slotId: slotId, type: "slot" };

        slotGroup.add(batteryGroup);

        // Smoke Anchor (World coordinate Vector3)
        const smokeAnchor = new THREE.Vector3(cfg.posX + posX, posY, posZ + 0.15);

        stationGroup.add(slotGroup);

        stationData.slots.push({
          stationId: cfg.id,
          slotId: slotId,
          group: slotGroup,
          batteryGroup: batteryGroup,
          batteryOledMesh: oledMesh,
          oledCanvas: oledCanvas,
          oledCtx: oCtx,
          ledRingMat: ledRingMat,
          slotLight: slotLight,
          alarmLight: alarmLight,
          smokeAnchor: smokeAnchor,
          isEjected: false,
          hasBattery: true,
          normalZ: 0,
          ejectedZ: 0.38,
        });
      }
    }

    scene.add(stationGroup);
    return stationData;
  }

  // Build both stations
  function buildCabinet(scene, configs = DEFAULT_CONFIGS) {
    const list = Array.isArray(configs) ? configs : [configs];
    list.forEach((cfg) => buildStationCabinet(scene, cfg));
    return {
      stations,
      slots: stations["TRAM-BK01"] ? stations["TRAM-BK01"].slots : Object.values(stations)[0]?.slots || [],
    };
  }

  function resolveSlot(stationIdOrSlotId, maybeSlotId) {
    if (typeof stationIdOrSlotId === "string" && typeof maybeSlotId === "number") {
      const st = stations[stationIdOrSlotId];
      return st?.slots.find((s) => s.slotId === maybeSlotId);
    }
    // Backward compatibility: single slotId passed
    const slotId = typeof stationIdOrSlotId === "number" ? stationIdOrSlotId : maybeSlotId;
    return getAllSlots().find((s) => s.slotId === slotId);
  }

  function setBatteryEjected(stationId, slotId, isEjected, animate = true) {
    const slot = resolveSlot(stationId, slotId);
    if (!slot) return;

    slot.isEjected = isEjected;
    const targetZ = isEjected ? slot.ejectedZ : slot.normalZ;

    if (!animate || typeof TWEEN === "undefined") {
      slot.batteryGroup.position.z = targetZ;
      return;
    }

    new TWEEN.Tween(slot.batteryGroup.position)
      .to({ z: targetZ }, 600)
      .easing(TWEEN.Easing.Cubic.Out)
      .start();
  }

  function setBatteryPresence(stationId, slotId, hasBattery, animate = true) {
    const slot = resolveSlot(stationId, slotId);
    if (!slot) return;

    slot.hasBattery = hasBattery;

    if (!animate || typeof TWEEN === "undefined") {
      slot.batteryGroup.visible = hasBattery;
      slot.batteryGroup.position.z = slot.normalZ;
      return;
    }

    if (hasBattery) {
      slot.batteryGroup.visible = true;
      slot.batteryGroup.position.set(0, 0, 0.9);
      new TWEEN.Tween(slot.batteryGroup.position)
        .to({ z: slot.normalZ }, 850)
        .easing(TWEEN.Easing.Back.Out)
        .start();
    } else {
      new TWEEN.Tween(slot.batteryGroup.position)
        .to({ z: 1.1 }, 650)
        .easing(TWEEN.Easing.Cubic.In)
        .onComplete(() => {
          slot.batteryGroup.visible = false;
        })
        .start();
    }
  }

  function setSlotLedColor(stationId, slotId, hexColor, intensity = 2.2) {
    const slot = resolveSlot(stationId, slotId);
    if (!slot) return;

    slot.ledRingMat.color.setHex(hexColor);
    slot.ledRingMat.emissive.setHex(hexColor);
    slot.ledRingMat.emissiveIntensity = intensity;

    slot.slotLight.color.setHex(hexColor);
    slot.slotLight.intensity = intensity * 0.6;
  }

  function updateBatteryDisplay(stationId, slotId, soc, isCharging) {
    const slot = resolveSlot(stationId, slotId);
    if (!slot || !slot.batteryOledMesh || !slot.oledCtx) return;

    const ctx = slot.oledCtx;
    ctx.fillStyle = "#000";
    ctx.fillRect(0, 0, 128, 64);

    ctx.fillStyle = isCharging ? "#f59e0b" : soc >= 90 ? "#10b981" : "#38bdf8";
    ctx.font = "bold 26px monospace";
    ctx.textAlign = "center";
    ctx.fillText(`${Math.round(soc)}%`, 64, 34);

    ctx.font = "bold 14px sans-serif";
    ctx.fillStyle = isCharging ? "#fbbf24" : soc >= 90 ? "#34d399" : "#94a3b8";
    ctx.fillText(isCharging ? "⚡ CHARGE" : soc >= 90 ? "READY" : "OK", 64, 54);

    slot.batteryOledMesh.material.map.needsUpdate = true;
  }

  function setAlarmLight(stationId, slotId, active, color = 0xef4444) {
    const slot = resolveSlot(stationId, slotId);
    if (!slot) return;
    slot.alarmLight.color.setHex(color);
    slot.alarmLight.intensity = active ? 3.5 : 0;
  }

  function getSlots(stationId = "TRAM-BK01") {
    return stations[stationId]?.slots || Object.values(stations)[0]?.slots || [];
  }

  function getAllSlots() {
    return Object.values(stations).flatMap((st) => st.slots);
  }

  return {
    buildCabinet,
    buildStationCabinet,
    setBatteryEjected,
    setBatteryPresence,
    setSlotLedColor,
    updateBatteryDisplay,
    updateKioskScreen,
    setAlarmLight,
    getSlots,
    getAllSlots,
    getStation: (id) => stations[id],
    getStationConfigs: () => DEFAULT_CONFIGS,
  };
})();
