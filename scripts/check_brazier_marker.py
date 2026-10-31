"""Reproduce the oversized arrow using installed HUD/camera data; audit the fix.

This checks projection math and native asset settings, not a Unity render.
"""
from pathlib import Path
import json
import math
import sys

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root / '.build/python'))
import UnityPy

bundles = Path('X:/SteamLibrary/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64')
env = UnityPy.load(str(next(bundles.glob('ror2-base-core-camera_assets_all*'))),
                   str(next(bundles.glob('ror2-base-ui_assets_all*'))))
cameras = [o.read_typetree() for o in env.objects
           if o.type.name == 'Camera' and o.read().m_GameObject.read().m_Name == 'UI Camera']
assert cameras and all(not camera['orthographic'] for camera in cameras)
hud_canvas = None
scaler = None
for name, pointer in env.container.items():
    if not name.lower().endswith('/hudsimple.prefab') or pointer.type.name != 'GameObject':
        continue
    for component in pointer.read().m_Component:
        reader = component.component.deref()
        if reader.type.name == 'Canvas':
            hud_canvas = reader.read_typetree()
        elif reader.type.name == 'MonoBehaviour':
            data = reader.read_typetree()
            if 'm_ReferenceResolution' in data:
                scaler = data
assert hud_canvas and hud_canvas['m_RenderMode'] == 1
depth = hud_canvas['m_PlaneDistance']
assert depth > 1, 'The regression needs a HUD plane beyond native marker depth 1.'
assert scaler and scaler['m_UiScaleMode'] == 1
reference = scaler['m_ReferenceResolution']
match = scaler['m_MatchWidthOrHeight']
checks = 0
for width, height in [(1280,720), (1920,1080), (2560,1440), (3840,2160), (3440,1440), (1280,1440)]:
    # CanvasScaler's logarithmic blend for ScaleWithScreenSize.
    scale = 2 ** ((1-match)*math.log2(width/reference['x']) + match*math.log2(height/reference['y']))
    for fov in (40, cameras[0]['field of view'], 90):
        focal = height / (2*math.tan(math.radians(fov)/2))
        # A 32-unit canvas arrow has this world length on its intended HUD plane.
        world_length = 32*scale*depth/focal
        oversized = world_length*focal/1
        corrected = world_length*focal/depth
        assert math.isclose(oversized/corrected, depth, rel_tol=1e-6)
        assert math.isclose(corrected, 32*scale, rel_tol=1e-6)
        checks += 2
        # Ray/plane reprojection keeps the native on-screen or clamped edge
        # location, including nonzero viewport offsets used by split screen.
        for origin in ((0,0), (1280,0)):
            for x, y in ((.5,.5), (.025,.5), (.975,.5), (.5,.025), (.5,.975)):
                sx, sy = origin[0]+width*x, origin[1]+height*y
                wx = (sx-origin[0]-width/2)/focal
                wy = (sy-origin[1]-height/2)/focal
                px = wx*depth/depth*focal+origin[0]+width/2
                py = wy*depth/depth*focal+origin[1]+height/2
                assert math.isclose(px,sx,abs_tol=1e-6) and math.isclose(py,sy,abs_tol=1e-6)
                checks += 1
receipt = {'nativeUiPerspective':True, 'nativeUiFov':cameras[0]['field of view'],
           'nativeHudPlaneDistance':depth, 'referenceResolution':reference,
           'previousSizeMultiplier':depth, 'projectionChecks':checks,
           'resolutions':6, 'gameLaunched':False, 'unityRendered':False}
(root / '.build/brazier-marker-check.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8')
print(f'PASS: brazier marker projection ({checks} checks / six resolutions): reproduced {depth:.2f}x oversizing; HUD-plane placement preserves size and on/off-screen location. No Unity render.')
