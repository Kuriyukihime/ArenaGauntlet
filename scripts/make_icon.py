"""Create the Thunderstore thumbnail from the approved generated cover artwork."""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent.parent
source = root / 'art/arena-cover.png'
if not source.is_file():
    raise FileNotFoundError(f'Missing cover artwork: {source}')
with Image.open(source) as artwork:
    if artwork.width != artwork.height:
        raise ValueError('Cover artwork must be square; do not crop it automatically.')
    artwork.convert('RGB').resize((256, 256), Image.Resampling.LANCZOS).save(root / 'package/icon.png', optimize=True)
print('Packaged 256px thumbnail from art/arena-cover.png.')
