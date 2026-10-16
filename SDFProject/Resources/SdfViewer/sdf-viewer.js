import { createRenderer } from './sdf-renderer.js';

const codeInput = document.getElementById('code');
let renderer = null;
try {
    renderer = createRenderer(document.getElementById('view'));
} catch (error) {
    codeInput.classList.add('error');
    codeInput.title = error.message;
}

function updateProgram() {
    const error = renderer.setCode(codeInput.value);
    codeInput.classList.toggle('error', error !== null);
    codeInput.title = error ?? '';
}

function setUpInput() {
    let compileTimer;
    codeInput.addEventListener('input', () => {
        clearTimeout(compileTimer);
        compileTimer = setTimeout(updateProgram, 300);
    });

    codeInput.addEventListener('keydown', event => {
        if (event.key !== 'Tab') return;
        event.preventDefault();
        codeInput.setRangeText('    ', codeInput.selectionStart, codeInput.selectionEnd, 'end');
        codeInput.dispatchEvent(new Event('input'));
    });
}

if (renderer) {
    setUpInput();
    updateProgram();
}
