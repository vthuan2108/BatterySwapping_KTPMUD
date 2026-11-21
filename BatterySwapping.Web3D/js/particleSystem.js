/**
 * particleSystem.js
 * 3D Particle Smoke & Fire Emitter for BSS Thermal Overheat Simulation (> 55°C)
 * Built with Three.js (r128)
 */

const ParticleSystem = (function () {
  const MAX_PARTICLES = 400;
  let particleGeo;
  let particleMat;
  let particlePoints;

  // Active emitters mapped by slotId -> { origin, active, timer }
  const activeEmitters = {};

  // Arrays to hold particle attributes
  const positions = new Float32Array(MAX_PARTICLES * 3);
  const colors = new Float32Array(MAX_PARTICLES * 3);
  const sizes = new Float32Array(MAX_PARTICLES);
  const alphas = new Float32Array(MAX_PARTICLES);

  // Internal state per particle
  const particleState = [];

  function createSmokeTexture() {
    const canvas = document.createElement("canvas");
    canvas.width = 64;
    canvas.height = 64;
    const ctx = canvas.getContext("2d");

    const grad = ctx.createRadialGradient(32, 32, 2, 32, 32, 30);
    grad.addColorStop(0, "rgba(255, 255, 255, 1.0)");
    grad.addColorStop(0.3, "rgba(240, 240, 240, 0.7)");
    grad.addColorStop(0.7, "rgba(100, 100, 100, 0.3)");
    grad.addColorStop(1, "rgba(0, 0, 0, 0)");

    ctx.fillStyle = grad;
    ctx.fillRect(0, 0, 64, 64);

    const texture = new THREE.CanvasTexture(canvas);
    texture.needsUpdate = true;
    return texture;
  }

  function init(scene) {
    particleGeo = new THREE.BufferGeometry();

    for (let i = 0; i < MAX_PARTICLES; i++) {
      positions[i * 3 + 0] = 0;
      positions[i * 3 + 1] = -999; // Hide off-screen initially
      positions[i * 3 + 2] = 0;

      colors[i * 3 + 0] = 1.0;
      colors[i * 3 + 1] = 0.4;
      colors[i * 3 + 2] = 0.1;

      sizes[i] = 0.05;
      alphas[i] = 0.0;

      particleState.push({
        alive: false,
        slotId: null,
        life: 0,
        maxLife: 1.5,
        vx: 0,
        vy: 0,
        vz: 0,
        size: 0.1,
        type: "smoke", // 'smoke' or 'spark'
      });
    }

    particleGeo.setAttribute("position", new THREE.BufferAttribute(positions, 3));
    particleGeo.setAttribute("color", new THREE.BufferAttribute(colors, 3));

    const texture = createSmokeTexture();

    particleMat = new THREE.PointsMaterial({
      size: 0.35,
      map: texture,
      transparent: true,
      opacity: 0.85,
      vertexColors: true,
      blending: THREE.NormalBlending,
      depthWrite: false,
    });

    particlePoints = new THREE.Points(particleGeo, particleMat);
    scene.add(particlePoints);
  }

  function startOverheat(slotId, origin) {
    activeEmitters[slotId] = {
      origin: origin.clone(),
      active: true,
    };
  }

  function stopOverheat(slotId) {
    if (activeEmitters[slotId]) {
      activeEmitters[slotId].active = false;
    }
  }

  function isOverheating(slotId) {
    return activeEmitters[slotId] && activeEmitters[slotId].active;
  }

  function spawnParticle(emitter, slotId) {
    // Find first dead particle
    for (let i = 0; i < MAX_PARTICLES; i++) {
      const p = particleState[i];
      if (!p.alive) {
        p.alive = true;
        p.slotId = slotId;
        p.life = 0;
        p.maxLife = 1.2 + Math.random() * 0.8;

        const isSpark = Math.random() < 0.2;
        p.type = isSpark ? "spark" : "smoke";

        // Spawn around slot origin with slight jitter
        positions[i * 3 + 0] = emitter.origin.x + (Math.random() - 0.5) * 0.4;
        positions[i * 3 + 1] = emitter.origin.y + (Math.random() - 0.5) * 0.2;
        positions[i * 3 + 2] = emitter.origin.z + 0.05 + Math.random() * 0.1;

        if (isSpark) {
          // Sparks pop outwards quickly
          p.vx = (Math.random() - 0.5) * 0.8;
          p.vy = 0.8 + Math.random() * 1.2;
          p.vz = 0.4 + Math.random() * 0.8;
          colors[i * 3 + 0] = 1.0;
          colors[i * 3 + 1] = 0.9;
          colors[i * 3 + 2] = 0.2;
        } else {
          // Smoke rises and drifts
          p.vx = (Math.random() - 0.5) * 0.25;
          p.vy = 0.45 + Math.random() * 0.55;
          p.vz = 0.15 + Math.random() * 0.25;
          // Initial fire/glow color
          colors[i * 3 + 0] = 0.95;
          colors[i * 3 + 1] = 0.35 + Math.random() * 0.2;
          colors[i * 3 + 2] = 0.05;
        }
        break;
      }
    }
  }

  function update(delta) {
    if (!particlePoints) return;

    // Spawn new particles from active emitters
    for (const slotId in activeEmitters) {
      const em = activeEmitters[slotId];
      if (em.active) {
        // Spawn 3-5 particles per tick
        const count = 3 + Math.floor(Math.random() * 3);
        for (let k = 0; k < count; k++) {
          spawnParticle(em, parseInt(slotId, 10));
        }
      }
    }

    // Update existing particles
    let posNeedsUpdate = false;
    let colNeedsUpdate = false;

    for (let i = 0; i < MAX_PARTICLES; i++) {
      const p = particleState[i];
      if (p.alive) {
        p.life += delta;

        if (p.life >= p.maxLife) {
          p.alive = false;
          positions[i * 3 + 1] = -999; // Hide
          posNeedsUpdate = true;
          continue;
        }

        const progress = p.life / p.maxLife;

        // Position movement
        positions[i * 3 + 0] += p.vx * delta;
        positions[i * 3 + 1] += p.vy * delta;
        positions[i * 3 + 2] += p.vz * delta;

        // Smoke transition: Fire orange -> Dark gray/black -> Fade
        if (p.type === "smoke") {
          if (progress > 0.2) {
            // Transition to gray smoke
            const t = (progress - 0.2) / 0.8;
            const gray = THREE.MathUtils.lerp(0.5, 0.12, t);
            colors[i * 3 + 0] = THREE.MathUtils.lerp(colors[i * 3 + 0], gray, 0.15);
            colors[i * 3 + 1] = THREE.MathUtils.lerp(colors[i * 3 + 1], gray, 0.15);
            colors[i * 3 + 2] = THREE.MathUtils.lerp(colors[i * 3 + 2], gray, 0.15);
            colNeedsUpdate = true;
          }
        }

        posNeedsUpdate = true;
      }
    }

    if (posNeedsUpdate) {
      particleGeo.attributes.position.needsUpdate = true;
    }
    if (colNeedsUpdate) {
      particleGeo.attributes.color.needsUpdate = true;
    }
  }

  return {
    init,
    startOverheat,
    stopOverheat,
    isOverheating,
    update,
  };
})();
