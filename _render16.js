const sharp = require('sharp');
const fs = require('fs');
(async () => {
  const svg = fs.readFileSync('Resources/LevyFlightBrandIcon.svg');
  // True 16x16 at native size, upscaled 32x with nearest-neighbor for inspection.
  const native = await sharp(svg).resize(16, 16, { fit: 'fill', kernel: 'lanczos3' }).png().toBuffer();
  await sharp(native).resize(512, 512, { kernel: 'nearest' }).png().toFile('_preview_16_big.png');
  console.log('done');
})();
