// Renders a scene with the IDE's renderer in its quad view, labels the views, and posts the PNG back to the server that served it.
import { createRenderer } from './sdf-renderer.js';

const post = (path, body) => fetch(path, { method: 'POST', body });

// Labels in the same order as the renderer's quad views: top left, top right, bottom left, bottom right.
const VIEWS = ['Side (from +X)', 'Top (from +Y)', 'Front (from +Z)', 'Perspective'];
const SCALE_BAR_LENGTHS = [0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100];

function metres(value) {
    return value >= 1 ? `${value} m` : `${Math.round(value * 100)} cm`;
}

// Sizes below are for a 1024 pixel image and scale with it.
let unit = 1;

function label(context, text, x, y) {
    context.font = `600 ${15 * unit}px -apple-system, "Segoe UI", sans-serif`;
    context.lineWidth = 4 * unit;
    context.strokeStyle = 'rgba(0, 0, 0, 0.75)';
    context.fillStyle = '#f0f0f4';
    context.strokeText(text, x, y);
    context.fillText(text, x, y);
}

// A bar of a round length, about a fifth of the view wide, so proportions can be read in metres.
function scaleBar(context, x, y, width, height, orthoScale) {
    const metresPerPixel = 2 * orthoScale / height;
    const length = SCALE_BAR_LENGTHS.filter(l => l / metresPerPixel <= width * 0.25).at(-1) ?? SCALE_BAR_LENGTHS[0];
    const pixels = length / metresPerPixel;
    const left = x + 12 * unit;
    const bottom = y + height - 14 * unit;

    context.lineWidth = 6 * unit;
    context.strokeStyle = 'rgba(0, 0, 0, 0.75)';
    context.beginPath();
    context.moveTo(left, bottom);
    context.lineTo(left + pixels, bottom);
    context.stroke();
    context.lineWidth = 2 * unit;
    context.strokeStyle = '#f0f0f4';
    context.stroke();
    label(context, metres(length), left, bottom - 8 * unit);
}

async function run() {
    const scene = await (await fetch('scene.json')).json();

    const view = document.getElementById('view');
    view.style.width = `${scene.size}px`;
    view.style.height = `${scene.size}px`;

    const renderer = createRenderer(view);
    const error = renderer.setCode(scene.code);
    if (error) throw new Error(`The shader does not compile:\n${error}`);
    renderer.setCamera(scene.camera);
    if (scene.light) renderer.setLight(scene.light);
    if (scene.style) renderer.setStyle(scene.style);
    renderer.setQuadView(true);
    renderer.renderNow();

    const output = document.createElement('canvas');
    output.width = view.width;
    output.height = view.height;
    const context = output.getContext('2d');
    unit = output.width / 1024;
    context.drawImage(view, 0, 0);

    const half = output.width / 2;
    VIEWS.forEach((name, i) => {
        const x = (i % 2) * half;
        const y = Math.floor(i / 2) * half;
        label(context, name, x + 12 * unit, y + 24 * unit);
        if (i < 3) scaleBar(context, x, y, half, half, scene.camera.orthoScale);
    });
    if (scene.caption) label(context, scene.caption, half + 12 * unit, output.height - 14 * unit);

    context.strokeStyle = '#000000';
    context.lineWidth = 2 * unit;
    context.beginPath();
    context.moveTo(half, 0);
    context.lineTo(half, output.height);
    context.moveTo(0, half);
    context.lineTo(output.width, half);
    context.stroke();

    const png = await new Promise(resolve => output.toBlob(resolve, 'image/png'));
    await post('result', png);
}

run().catch(error => post('error', error?.message ?? String(error)));
