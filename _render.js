const sharp = require('sharp');
const fs = require('fs');
(async () => {
  const svg = fs.readFileSync('Resources/LevyFlightBrandIcon.svg');
  // Composite onto a light background so the dark strokes are visible,
  // and render large enough to inspect detail.
  const big = await sharp(svg).resize(512, 512, { fit: 'contain' }).png().toBuffer();
  await sharp({
    create: { width: 512, height: 512, channels: 4, background: { r: 245, g: 245, b: 245, alpha: 1 } }
  })
    .composite([{ input: big, gravity: 'centre' }])
    .png()
    .toFile('_preview.png');
  // Also a true 16x16 at native size.
  await sharp(svg).resize(16, 16, { fit: 'fill', kernel: 'lanczos3' }).png().toFile('_preview_16.png');
  console.log('done');
})();
