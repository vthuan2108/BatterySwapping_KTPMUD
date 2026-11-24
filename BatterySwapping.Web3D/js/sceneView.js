const SceneView = (() => {
  let scene, camera, renderer, controls;
  const dom = {};

  function setupThreeScene() {
    const width = dom.container.clientWidth;
    const height = dom.container.clientHeight;

    scene = new THREE.Scene();
    scene.background = new THREE.Color(0x070c18);
    scene.fog = new THREE.FogExp2(0x070c18, 0.025);

    camera = new THREE.PerspectiveCamera(45, width / height, 0.1, 100);
    // Centered view showing both stations side-by-side
    camera.position.set(0, 1.6, 7.5);

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
    controls.minDistance = 1.8;
    controls.maxDistance = 18.0;
    controls.target.set(0, 0.4, 0);
  }

  function setupLighting() {
    // Ambient Light
    const ambientLight = new THREE.AmbientLight(0x64748b, 0.95);
    scene.add(ambientLight);

    // Key Studio Light covering both stations
    const dirLight = new THREE.DirectionalLight(0xffffff, 1.35);
    dirLight.position.set(6, 10, 8);
    dirLight.castShadow = true;
    dirLight.shadow.mapSize.width = 2048;
    dirLight.shadow.mapSize.height = 2048;
    dirLight.shadow.camera.near = 0.5;
    dirLight.shadow.camera.far = 25;
    dirLight.shadow.bias = -0.0005;
    const d = 7.0;
    dirLight.shadow.camera.left = -d;
    dirLight.shadow.camera.right = d;
    dirLight.shadow.camera.top = d;
    dirLight.shadow.camera.bottom = -d;
    scene.add(dirLight);

    // Front soft fill
    const frontFill = new THREE.DirectionalLight(0x38bdf8, 0.7);
    frontFill.position.set(0, 2, 7);
    scene.add(frontFill);

    // Left Rim Light for Station 1
    const rimLeft = new THREE.DirectionalLight(0x0284c7, 0.6);
    rimLeft.position.set(-8, 4, -4);
    scene.add(rimLeft);

    // Right Rim Light for Station 2
    const rimRight = new THREE.DirectionalLight(0x10b981, 0.6);
    rimRight.position.set(8, 4, -4);
    scene.add(rimRight);
  }

  function init(container) {
    dom.container = container;
    setupThreeScene();
    setupLighting();
    return { scene, camera, renderer, controls };
  }

  return { init };
})();
