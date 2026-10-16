const VERTEX_SHADER = `#version 300 es
in vec2 position;
void main() { gl_Position = vec4(position, 0.0, 1.0); }`;

const fragmentShader = sdfCode => `#version 300 es
precision highp float;
precision highp int;

uniform vec2 uResolution;
uniform vec2 uViewportOrigin;
uniform vec3 uCameraPosition;
uniform vec3 uCameraTarget;
uniform vec3 uCameraUp;
uniform float uOrthoScale;
uniform vec3 uLightPosition;

out vec4 fragColor;

#line 1
${sdfCode}

const int MAX_STEPS = 200;
const float MAX_DISTANCE = 100.0;
const float SURFACE_DISTANCE = 0.001;

float map(vec3 p) { return sdf_scene(p); }

float raymarch(vec3 ro, vec3 rd)
{
    float t = 0.0;
    for (int i = 0; i < MAX_STEPS; i++)
    {
        vec3 p = ro + rd * t;
        float d = map(p);
        if (d < SURFACE_DISTANCE || t > MAX_DISTANCE) break;
        t += d;
    }
    return t;
}

vec3 calcNormal(vec3 p)
{
    const vec2 k = vec2(1.0, -1.0);
    const float e = 0.0005;
    return normalize(
        k.xyy * map(p + k.xyy * e) +
        k.yyx * map(p + k.yyx * e) +
        k.yxy * map(p + k.yxy * e) +
        k.xxx * map(p + k.xxx * e));
}

float softShadow(vec3 ro, vec3 rd, float maxT)
{
    float result = 1.0;
    float t = 0.02;
    for (int i = 0; i < 64 && t < maxT; i++)
    {
        float d = map(ro + rd * t);
        if (d < SURFACE_DISTANCE) return 0.0;
        result = min(result, 8.0 * d / t);
        t += clamp(d, 0.01, 0.5);
    }
    return clamp(result, 0.0, 1.0);
}

float ambientOcclusion(vec3 p, vec3 n)
{
    float occlusion = 0.0;
    float weight = 1.0;
    for (int i = 1; i <= 5; i++)
    {
        float h = 0.03 * float(i);
        occlusion += (h - map(p + n * h)) * weight;
        weight *= 0.85;
    }
    return clamp(1.0 - 4.0 * occlusion, 0.0, 1.0);
}

vec3 background(vec3 rd)
{
    return mix(vec3(0.09, 0.09, 0.11), vec3(0.22, 0.24, 0.3), clamp(rd.y * 0.5 + 0.5, 0.0, 1.0));
}

void main()
{
    vec2 uv = (gl_FragCoord.xy - uViewportOrigin - 0.5 * uResolution) / uResolution.y;

    vec3 forward = normalize(uCameraTarget - uCameraPosition);
    vec3 right = normalize(cross(forward, uCameraUp));
    vec3 up = cross(right, forward);
    bool orthographic = uOrthoScale > 0.0;
    vec3 ro = orthographic ? uCameraPosition + 2.0 * uOrthoScale * (uv.x * right + uv.y * up) : uCameraPosition;
    vec3 rd = orthographic ? forward : normalize(uv.x * right + uv.y * up + 1.6 * forward);

    float t = raymarch(ro, rd);
    if (t > MAX_DISTANCE)
    {
        fragColor = vec4(pow(background(rd), vec3(1.0 / 2.2)), 1.0);
        return;
    }

    vec3 p = ro + rd * t;
    vec3 n = calcNormal(p);

    vec3 toLight = uLightPosition - p;
    float lightDistance = length(toLight);
    vec3 l = toLight / lightDistance;
    vec3 h = normalize(l - rd);

    float attenuation = 1.0 / (1.0 + 0.02 * lightDistance * lightDistance);
    float shadow = softShadow(p + n * 0.002, l, lightDistance);
    float diffuse = max(dot(n, l), 0.0);
    float specular = pow(max(dot(n, h), 0.0), 48.0) * diffuse;
    float occlusion = ambientOcclusion(p, n);

    vec3 albedo = sdf_material(p);
    vec3 lightColor = vec3(1.0, 0.95, 0.88) * 3.5;
    vec3 color = albedo * vec3(0.12, 0.13, 0.17) * occlusion
               + (albedo * diffuse + 0.5 * specular) * lightColor * attenuation * shadow;

    color = 1.0 - exp(-1.4 * color);

    if (!orthographic)
    {
        float fog = 1.0 - exp(-0.002 * t * t);
        color = mix(color, background(rd), fog);
    }

    fragColor = vec4(pow(color, vec3(1.0 / 2.2)), 1.0);
}`;

export function createRenderer(canvas) {
    const gl = canvas.getContext('webgl2', { preserveDrawingBuffer: true });
    if (!gl) throw new Error('This browser does not support WebGL2');

    const camera = { yaw: 0.7, pitch: 0.35, distance: 8, target: [0, 0, 0] };
    const lightPosition = [4, 6, 3];
    let program = null;
    let quadView = false;
    let rotateView = false;

    function compile(type, source) {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
            const log = gl.getShaderInfoLog(shader);
            gl.deleteShader(shader);
            throw new Error(log);
        }
        return shader;
    }

    function createProgram(sdfCode) {
        const vertex = compile(gl.VERTEX_SHADER, VERTEX_SHADER);
        const fragment = compile(gl.FRAGMENT_SHADER, fragmentShader(sdfCode));
        const created = gl.createProgram();
        gl.attachShader(created, vertex);
        gl.attachShader(created, fragment);
        gl.bindAttribLocation(created, 0, 'position');
        gl.linkProgram(created);
        gl.deleteShader(vertex);
        gl.deleteShader(fragment);
        if (!gl.getProgramParameter(created, gl.LINK_STATUS)) {
            const log = gl.getProgramInfoLog(created);
            gl.deleteProgram(created);
            throw new Error(log);
        }
        return created;
    }

    function setCode(sdfCode) {
        try {
            const created = createProgram(sdfCode);
            if (program) gl.deleteProgram(program);
            program = created;
            requestRender();
            return null;
        } catch (error) {
            console.error(error.message);
            return error.message;
        }
    }

    function cameraPosition() {
        const { yaw, pitch, distance, target } = camera;
        return [
            target[0] + distance * Math.cos(pitch) * Math.sin(yaw),
            target[1] + distance * Math.sin(pitch),
            target[2] + distance * Math.cos(pitch) * Math.cos(yaw),
        ];
    }

    const orbitView = () => ({ position: cameraPosition(), up: [0, 1, 0], orthoScale: 0 });

    function axisView(direction, up) {
        const far = 50;
        return {
            position: camera.target.map((value, i) => value + direction[i] * far),
            up,
            orthoScale: camera.distance * 0.5 / 1.6,
        };
    }

    const quadViews = () => [
        axisView([1, 0, 0], [0, 1, 0]),
        axisView([0, 1, 0], [0, 0, -1]),
        axisView([0, 0, 1], [0, 1, 0]),
        orbitView(),
    ];

    function resize() {
        const scale = window.devicePixelRatio || 1;
        const width = Math.round(canvas.clientWidth * scale);
        const height = Math.round(canvas.clientHeight * scale);
        if (canvas.width === width && canvas.height === height) return false;
        canvas.width = width;
        canvas.height = height;
        return true;
    }

    function drawView(view, x, y, width, height) {
        gl.viewport(x, y, width, height);
        gl.uniform2f(gl.getUniformLocation(program, 'uResolution'), width, height);
        gl.uniform2f(gl.getUniformLocation(program, 'uViewportOrigin'), x, y);
        gl.uniform3fv(gl.getUniformLocation(program, 'uCameraPosition'), view.position);
        gl.uniform3fv(gl.getUniformLocation(program, 'uCameraTarget'), camera.target);
        gl.uniform3fv(gl.getUniformLocation(program, 'uCameraUp'), view.up);
        gl.uniform1f(gl.getUniformLocation(program, 'uOrthoScale'), view.orthoScale);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    const ROTATION_SPEED = Math.PI / 6;
    let lastFrameTime = null;

    function render(time) {
        renderRequested = false;
        if (rotateView) {
            if (lastFrameTime !== null) {
                camera.yaw += (time - lastFrameTime) / 1000 * ROTATION_SPEED;
                orbitChanged = true;
            }
            lastFrameTime = time;
            requestRender(false);
        } else {
            lastFrameTime = null;
        }

        const all = resize() || allChanged;
        const orbit = all || orbitChanged;
        allChanged = orbitChanged = false;
        if (!program || !orbit) return;

        gl.useProgram(program);
        gl.uniform3fv(gl.getUniformLocation(program, 'uLightPosition'), lightPosition);
        if (!quadView) {
            drawView(orbitView(), 0, 0, canvas.width, canvas.height);
            return;
        }

        const left = Math.floor(canvas.width / 2);
        const bottom = Math.floor(canvas.height / 2);
        const cells = [
            [0, bottom, left, canvas.height - bottom],
            [left, bottom, canvas.width - left, canvas.height - bottom],
            [0, 0, left, bottom],
            [left, 0, canvas.width - left, bottom],
        ];
        const views = quadViews();
        const first = all ? 0 : 3;
        for (let i = first; i < 4; i++) drawView(views[i], ...cells[i]);
    }

    let renderRequested = false;
    let allChanged = false;
    let orbitChanged = false;
    function requestRender(all = true) {
        if (all) allChanged = true;
        else orbitChanged = true;
        if (!renderRequested) {
            renderRequested = true;
            requestAnimationFrame(render);
        }
    }

    function setQuadView(enabled) {
        quadView = enabled;
        requestRender();
    }

    function setRotateView(enabled) {
        rotateView = enabled;
        requestRender(false);
    }

    gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);

    let dragging = null;
    canvas.addEventListener('pointerdown', event => {
        dragging = { x: event.clientX, y: event.clientY };
        canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener('pointermove', event => {
        if (!dragging) return;
        camera.yaw -= (event.clientX - dragging.x) * 0.01;
        camera.pitch = Math.max(-1.5, Math.min(1.5, camera.pitch + (event.clientY - dragging.y) * 0.01));
        dragging = { x: event.clientX, y: event.clientY };
        requestRender(false);
    });
    canvas.addEventListener('pointerup', () => dragging = null);
    canvas.addEventListener('pointercancel', () => dragging = null);

    canvas.addEventListener('wheel', event => {
        event.preventDefault();
        camera.distance = Math.max(0.5, Math.min(60, camera.distance * Math.exp(event.deltaY * 0.001)));
        requestRender();
    }, { passive: false });

    const resizeObserver = new ResizeObserver(() => requestRender());
    resizeObserver.observe(canvas);

    return {
        setCode,
        setQuadView,
        setRotateView,
        dispose: () => {
            rotateView = false;
            resizeObserver.disconnect();
        },
    };
}
