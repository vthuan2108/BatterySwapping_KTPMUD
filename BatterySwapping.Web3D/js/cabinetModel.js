/**
 * cabinetModel.js - V2 (Real-world VinFast / Gogoro Style BSS Station)
 * 3D Model Generator for BSS 8-Slot Battery Swapping Station
 * Featuring:
 * - Vibrant Electric Blue metallic enclosure with white border styling
 * - Integrated Security Camera at top-right
 * - Center Brand Emblem (V / BSS)
 * - Embedded Touchscreen Kiosk Display on cabinet with real-time dynamic texture
 * - 8 Receptacle Slots with illuminated LED perimeter rings
 * - Highly detailed front-facing Modular Battery Packs with horizontal ergonomic handles
 * - Eject / Slide-out physical animations
 */

const CabinetModel = (function () {
  let materials = {};
  let slotObjects = [];
  let kioskScreenMesh = null;
  let kioskCanvas = null;
  let kioskCtx = null;
  let kioskTexture = null;

  function initMaterials() {
    // VinFast / Electric Blue Metallic
    materials.cabinetBlue = new THREE.MeshStandardMaterial({
      color: 0x006ec7,
      metalness: 0.65,
      roughness: 0.3,
    });

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

  // Realtime Kiosk Screen on Cabinet Front
  function initKioskScreenTexture() {
    kioskCanvas = document.createElement("canvas");
    kioskCanvas.width = 512;
    kioskCanvas.height = 340;
    kioskCtx = kioskCanvas.getContext("2d");

    kioskTexture = new THREE.CanvasTexture(kioskCanvas);
    kioskTexture.needsUpdate = true;
    updateKioskScreen("STANDBY", "CHÀO MỪNG ĐẾN VỚI TRẠM ĐỔI PIN", "Bấm 'Bắt đầu đổi pin' hoặc Quét QR", "#10b981");
  }

  function updateKioskScreen(stepCode, mainText, subText, accentColor = "#38bdf8", slotNumber = null) {
    if (!kioskCtx) return;

    kioskCtx.fillStyle = "#050b17";
    kioskCtx.fillRect(0, 0, kioskCanvas.width, kioskCanvas.height);

    // Header bar
    kioskCtx.fillStyle = "#0284c7";
    kioskCtx.fillRect(0, 0, kioskCanvas.width, 50);

    kioskCtx.fillStyle = "#ffffff";
    kioskCtx.font = "bold 22px sans-serif";
    kioskCtx.textAlign = "center";
    kioskCtx.fillText("⚡ BSS SMART KIOSK • TRAM-BK01", kioskCanvas.width / 2, 34);

    // Outer border
    kioskCtx.strokeStyle = accentColor;
    kioskCtx.lineWidth = 6;
    kioskCtx.strokeRect(3, 3, kioskCanvas.width - 6, kioskCanvas.height - 6);

    // Main Content based on step
    if (stepCode === "STANDBY") {
      // Draw QR Code representation
      drawMockQrCode(kioskCtx, 60, 80, 140);

      kioskCtx.fillStyle = "#f8fafc";
      kioskCtx.font = "bold 24px sans-serif";
      kioskCtx.textAlign = "left";
      kioskCtx.fillText("QUÉT MÃ QR", 230, 115);

      kioskCtx.fillStyle = "#94a3b8";
      kioskCtx.font = "16px sans-serif";
      kioskCtx.fillText("hoặc chọn trên màn hình", 230, 145);
      kioskCtx.fillText("để bắt đầu chu trình", 230, 170);

      kioskCtx.fillStyle = "#38bdf8";
      kioskCtx.font = "bold 18px monospace";
      kioskCtx.fillText("Phí đổi: 25.000 đ/lần", 230, 205);

      // Status pill at bottom
      kioskCtx.fillStyle = "rgba(16, 185, 129, 0.2)";
      kioskCtx.fillRect(30, 255, kioskCanvas.width - 60, 55);
      kioskCtx.fillStyle = "#10b981";
      kioskCtx.font = "bold 20px sans-serif";
      kioskCtx.textAlign = "center";
      kioskCtx.fillText("✔ SẴN SÀNG PHỤC VỤ (8 NGĂN SẠC)", kioskCanvas.width / 2, 290);
    } else {
      // Active Swap Step Display
      kioskCtx.fillStyle = accentColor;
      kioskCtx.font = "bold 24px sans-serif";
      kioskCtx.textAlign = "center";
      kioskCtx.fillText(mainText, kioskCanvas.width / 2, 110);

      if (slotNumber) {
        // Big slot badge
        kioskCtx.fillStyle = "rgba(255, 255, 255, 0.1)";
        kioskCtx.fillRect(kioskCanvas.width / 2 - 80, 135, 160, 90);
        kioskCtx.strokeStyle = accentColor;
        kioskCtx.lineWidth = 4;
        kioskCtx.strokeRect(kioskCanvas.width / 2 - 80, 135, 160, 90);

        kioskCtx.fillStyle = "#ffffff";
        kioskCtx.font = "bold 48px monospace";
        kioskCtx.fillText(`Ô #${slotNumber}`, kioskCanvas.width / 2, 200);
      }

      kioskCtx.fillStyle = "#94a3b8";
      kioskCtx.font = "18px sans-serif";
      kioskCtx.textAlign = "center";
      kioskCtx.fillText(subText, kioskCanvas.width / 2, 275);

      // Progress dots
      kioskCtx.fillStyle = accentColor;
      kioskCtx.beginPath();
      kioskCtx.arc(kioskCanvas.width / 2, 310, 8, 0, Math.PI * 2);
      kioskCtx.fill();
    }

    if (kioskTexture) {
      kioskTexture.needsUpdate = true;
    }
  }

  function drawMockQrCode(ctx, x, y, size) {
    ctx.fillStyle = "#ffffff";
    ctx.fillRect(x, y, size, size);

    ctx.fillStyle = "#050b17";
    // Finder patterns
    drawFinder(ctx, x + 8, y + 8, 36);
    drawFinder(ctx, x + size - 44, y + 8, 36);
    drawFinder(ctx, x + 8, y + size - 44, 36);

    // Random pattern dots
    ctx.fillStyle = "#050b17";
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

  function buildCabinet(scene) {
    initMaterials();
    initKioskScreenTexture();

    const cabinetGroup = new THREE.Group();
    cabinetGroup.name = "BSS_Cabinet";

    // Overall Tower Dimension (Sleek VinFast Proportions)
    const towerW = 2.2;
    const towerH = 4.2;
    const towerD = 1.15;

    // 1. Base plinth
    const plinthGeo = new THREE.BoxGeometry(towerW + 0.15, 0.25, towerD + 0.15);
    const plinthMesh = new THREE.Mesh(plinthGeo, materials.cabinetDark);
    plinthMesh.position.y = -1.75;
    plinthMesh.receiveShadow = true;
    cabinetGroup.add(plinthMesh);

    // 2. Main Tower Enclosure (VinFast Blue)
    const towerGeo = new THREE.BoxGeometry(towerW, towerH - 0.25, towerD);
    const towerMesh = new THREE.Mesh(towerGeo, materials.cabinetBlue);
    towerMesh.position.y = 0.2;
    towerMesh.castShadow = true;
    towerMesh.receiveShadow = true;
    cabinetGroup.add(towerMesh);

    // 3. Top Canopy Cap (Slanted aerodynamic roof)
    const roofGeo = new THREE.BoxGeometry(towerW + 0.05, 0.2, towerD + 0.05);
    const roofMesh = new THREE.Mesh(roofGeo, materials.cabinetBlue);
    roofMesh.position.y = towerH / 2 + 0.15;
    cabinetGroup.add(roofMesh);

    // 4. White Accent Frame Line on front face (matching VinFast design)
    const frontZ = towerD / 2 + 0.005;
    const stripeMat = materials.whiteStripe;
    const stripeThick = 0.035;

    // Outer white perimeter loop
    const topStripe = new THREE.Mesh(new THREE.BoxGeometry(towerW - 0.2, stripeThick, 0.01), stripeMat);
    topStripe.position.set(0, 1.9, frontZ);
    const btmStripe = new THREE.Mesh(new THREE.BoxGeometry(towerW - 0.2, stripeThick, 0.01), stripeMat);
    btmStripe.position.set(0, -1.5, frontZ);
    const leftStripe = new THREE.Mesh(new THREE.BoxGeometry(stripeThick, 3.4, 0.01), stripeMat);
    leftStripe.position.set(-(towerW - 0.2) / 2, 0.2, frontZ);
    const rightStripe = new THREE.Mesh(new THREE.BoxGeometry(stripeThick, 3.4, 0.01), stripeMat);
    rightStripe.position.set((towerW - 0.2) / 2, 0.2, frontZ);

    cabinetGroup.add(topStripe);
    cabinetGroup.add(btmStripe);
    cabinetGroup.add(leftStripe);
    cabinetGroup.add(rightStripe);

    // 5. Brand Logo V at Top Center
    const logoGroup = new THREE.Group();
    logoGroup.position.set(0, 1.55, frontZ + 0.01);
    // Silver shield background
    const shieldGeo = new THREE.BoxGeometry(0.38, 0.32, 0.02);
    const shieldMesh = new THREE.Mesh(shieldGeo, materials.cabinetDark);
    logoGroup.add(shieldMesh);

    // Silver V-wings
    const wingGeo = new THREE.CylinderGeometry(0.015, 0.015, 0.22, 16);
    const leftWing = new THREE.Mesh(wingGeo, materials.cameraSilver);
    leftWing.position.set(-0.06, 0.02, 0.02);
    leftWing.rotation.z = -Math.PI / 5;
    const rightWing = new THREE.Mesh(wingGeo, materials.cameraSilver);
    rightWing.position.set(0.06, 0.02, 0.02);
    rightWing.rotation.z = Math.PI / 5;
    logoGroup.add(leftWing);
    logoGroup.add(rightWing);
    cabinetGroup.add(logoGroup);

    // 6. Security Camera at Top Right
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
    cabinetGroup.add(camGroup);

    // 7. Embedded Touchscreen Kiosk Display
    const screenFrameGeo = new THREE.BoxGeometry(0.88, 0.62, 0.03);
    const screenFrame = new THREE.Mesh(screenFrameGeo, materials.cabinetDark);
    screenFrame.position.set(0, 0.95, frontZ + 0.01);

    const screenMat = new THREE.MeshBasicMaterial({ map: kioskTexture });
    const screenGeo = new THREE.PlaneGeometry(0.82, 0.54);
    kioskScreenMesh = new THREE.Mesh(screenGeo, screenMat);
    kioskScreenMesh.position.set(0, 0, 0.018);
    screenFrame.add(kioskScreenMesh);
    cabinetGroup.add(screenFrame);

    // Brand Name Text: "SMART BSS" near bottom
    const brandCanvas = document.createElement("canvas");
    brandCanvas.width = 512;
    brandCanvas.height = 100;
    const bCtx = brandCanvas.getContext("2d");
    bCtx.fillStyle = "#ffffff";
    bCtx.font = "bold 52px sans-serif";
    bCtx.textAlign = "center";
    bCtx.letterSpacing = "6px";
    bCtx.fillText("S M A R T   B S S", 256, 65);
    const brandTex = new THREE.CanvasTexture(brandCanvas);
    const brandMat = new THREE.MeshBasicMaterial({ map: brandTex, transparent: true });
    const brandMesh = new THREE.Mesh(new THREE.PlaneGeometry(1.2, 0.24), brandMat);
    brandMesh.position.set(0, -1.35, frontZ + 0.01);
    cabinetGroup.add(brandMesh);

    // 8. Ground Floor
    const floorGeo = new THREE.PlaneGeometry(16, 16);
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

    const gridHelper = new THREE.GridHelper(14, 28, 0x0284c7, 0x1e293b);
    gridHelper.position.y = -1.87;
    scene.add(gridHelper);

    // ==============================================================
    // 9. 8 Receptacle Battery Slots (2 Columns x 4 Rows)
    // Placed on lower half of cabinet (matching reference photo)
    // ==============================================================
    const colX = [-0.52, 0.52];
    const rowY = [0.35, -0.15, -0.65, -1.15];
    const slotW = 0.72;
    const slotH = 0.40;
    const slotD = 0.85;

    slotObjects = [];
    let slotCounter = 1;

    for (let r = 0; r < 4; r++) {
      for (let c = 0; c < 2; c++) {
        const slotId = slotCounter++;
        const posX = colX[c];
        const posY = rowY[r];
        const posZ = frontZ;

        const slotGroup = new THREE.Group();
        slotGroup.position.set(posX, posY, posZ);
        slotGroup.name = `Slot_${slotId}`;

        // Slot Receptacle Bezel (Outer rounded border)
        const bezelGeo = new THREE.BoxGeometry(slotW + 0.04, slotH + 0.04, 0.04);
        const bezelMesh = new THREE.Mesh(bezelGeo, materials.cabinetDark);
        bezelMesh.position.z = -0.01;
        slotGroup.add(bezelMesh);

        // Slot Inner Cavity
        const cavGeo = new THREE.BoxGeometry(slotW - 0.04, slotH - 0.04, slotD);
        const cavMesh = new THREE.Mesh(cavGeo, materials.slotCavity);
        cavMesh.position.z = -slotD / 2;
        slotGroup.add(cavMesh);

        // Gold Charging Terminals at back of slot
        const pin1 = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.015, 0.06, 16), materials.goldPin);
        pin1.rotation.x = Math.PI / 2;
        pin1.position.set(-0.12, -0.08, -slotD + 0.03);
        const pin2 = pin1.clone();
        pin2.position.x = 0.12;
        slotGroup.add(pin1);
        slotGroup.add(pin2);

        // LED Perimeter Ring (Matching the glowing lime-green/cyan border in photo)
        const ledRingMat = new THREE.MeshStandardMaterial({
          color: 0x84cc16,
          emissive: 0x84cc16,
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

        // Internal Spot / Pointlight for the slot
        const slotLight = new THREE.PointLight(0x84cc16, 1.2, 1.2);
        slotLight.position.set(0, 0, -0.2);
        slotGroup.add(slotLight);

        // Overheat Alarm Spotlight
        const alarmLight = new THREE.PointLight(0xef4444, 0, 2.5);
        alarmLight.position.set(0, 0, 0.4);
        slotGroup.add(alarmLight);

        // ==============================================================
        // Detailed Front-Facing Battery Pack 3D
        // Clearly visible from front with horizontal handle like in photo!
        // ==============================================================
        const batteryGroup = new THREE.Group();
        batteryGroup.position.set(0, 0, 0);
        batteryGroup.name = `Battery_${slotId}`;

        const batW = slotW - 0.08;
        const batH = slotH - 0.08;
        const batD = slotD - 0.05;

        // Battery main chassis (slides into slot)
        const batBodyGeo = new THREE.BoxGeometry(batW, batH, batD);
        const batBodyMesh = new THREE.Mesh(batBodyGeo, materials.batteryBlue);
        batBodyMesh.position.z = -batD / 2 + 0.02;
        batteryGroup.add(batBodyMesh);

        // Battery Front Face Cap (Prominent faceplate facing user)
        const faceGeo = new THREE.BoxGeometry(batW + 0.01, batH + 0.01, 0.04);
        const faceMesh = new THREE.Mesh(faceGeo, materials.batteryBlue);
        faceMesh.position.z = 0.02;
        batteryGroup.add(faceMesh);

        // Upper and Lower indentations for grip
        const indentMat = materials.batteryHandle;
        const upperIndent = new THREE.Mesh(new THREE.BoxGeometry(batW * 0.75, 0.06, 0.03), indentMat);
        upperIndent.position.set(0, 0.08, 0.035);
        const lowerIndent = new THREE.Mesh(new THREE.BoxGeometry(batW * 0.75, 0.06, 0.03), indentMat);
        lowerIndent.position.set(0, -0.08, 0.035);
        batteryGroup.add(upperIndent);
        batteryGroup.add(lowerIndent);

        // Big Horizontal Handle (Bar handle right in center like photo!)
        const handleBarGeo = new THREE.BoxGeometry(batW * 0.68, 0.055, 0.055);
        const handleBar = new THREE.Mesh(handleBarGeo, materials.batteryHandle);
        handleBar.position.set(0, 0, 0.055);
        batteryGroup.add(handleBar);

        // Battery Mini OLED / SOC Indicator on handle center
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
        faceMesh.userData = { slotId: slotId, type: "battery" };
        handleBar.userData = { slotId: slotId, type: "handle" };
        cavMesh.userData = { slotId: slotId, type: "slot" };

        slotGroup.add(batteryGroup);

        // Smoke Anchor (Right at the slot front rim)
        const smokeAnchor = new THREE.Vector3(posX, posY, posZ + 0.15);

        cabinetGroup.add(slotGroup);

        slotObjects.push({
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
          ejectedZ: 0.38, // Pops forward 38cm when ejected/ready to pull
        });
      }
    }

    scene.add(cabinetGroup);
    return { cabinetGroup, slots: slotObjects };
  }

  // Eject (pop forward) or Insert (dock back) animation
  function setBatteryEjected(slotId, isEjected, animate = true) {
    const slot = slotObjects.find((s) => s.slotId === slotId);
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

  // Presence of battery inside slot (empty vs loaded)
  function setBatteryPresence(slotId, hasBattery, animate = true) {
    const slot = slotObjects.find((s) => s.slotId === slotId);
    if (!slot) return;

    slot.hasBattery = hasBattery;

    if (!animate || typeof TWEEN === "undefined") {
      slot.batteryGroup.visible = hasBattery;
      slot.batteryGroup.position.z = slot.normalZ;
      return;
    }

    if (hasBattery) {
      // Pin cắm vào: hiện từ phía trước bay vào hộc
      slot.batteryGroup.visible = true;
      slot.batteryGroup.position.set(0, 0, 0.9);

      new TWEEN.Tween(slot.batteryGroup.position)
        .to({ z: slot.normalZ }, 850)
        .easing(TWEEN.Easing.Back.Out)
        .start();
    } else {
      // Pin rút ra: trượt ra ngoài xa rồi biến mất
      new TWEEN.Tween(slot.batteryGroup.position)
        .to({ z: 1.1 }, 650)
        .easing(TWEEN.Easing.Cubic.In)
        .onComplete(() => {
          slot.batteryGroup.visible = false;
        })
        .start();
    }
  }

  // Update LED perimeter ring color
  function setSlotLedColor(slotId, hexColor, intensity = 2.2) {
    const slot = slotObjects.find((s) => s.slotId === slotId);
    if (!slot) return;

    slot.ledRingMat.color.setHex(hexColor);
    slot.ledRingMat.emissive.setHex(hexColor);
    slot.ledRingMat.emissiveIntensity = intensity;

    slot.slotLight.color.setHex(hexColor);
    slot.slotLight.intensity = intensity * 0.6;
  }

  // Update Battery Mini OLED Display
  function updateBatteryDisplay(slotId, soc, isCharging) {
    const slot = slotObjects.find((s) => s.slotId === slotId);
    if (!slot || !slot.batteryOledMesh || !slot.oledCtx) return;

    const ctx = slot.oledCtx;
    ctx.fillStyle = "#000";
    ctx.fillRect(0, 0, 128, 64);

    ctx.fillStyle = isCharging ? "#f59e0b" : (soc >= 90 ? "#10b981" : "#38bdf8");
    ctx.font = "bold 26px monospace";
    ctx.textAlign = "center";
    ctx.fillText(`${Math.round(soc)}%`, 64, 34);

    ctx.font = "bold 14px sans-serif";
    ctx.fillStyle = isCharging ? "#fbbf24" : (soc >= 90 ? "#34d399" : "#94a3b8");
    ctx.fillText(isCharging ? "⚡ CHARGE" : (soc >= 90 ? "READY" : "OK"), 64, 54);

    slot.batteryOledMesh.material.map.needsUpdate = true;
  }

  function setAlarmLight(slotId, active) {
    const slot = slotObjects.find((s) => s.slotId === slotId);
    if (!slot) return;
    slot.alarmLight.intensity = active ? 3.5 : 0;
  }

  return {
    buildCabinet,
    setBatteryEjected,
    setBatteryPresence,
    setSlotLedColor,
    updateBatteryDisplay,
    updateKioskScreen,
    setAlarmLight,
    getSlots: () => slotObjects,
  };
})();
