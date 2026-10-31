"""Offline checks of installed assets implicated in the 0.3 review failures."""
import json
import sys
from collections import deque
from pathlib import Path

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root / '.build/python'))
import UnityPy

bundles = Path('X:/SteamLibrary/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64')
env = UnityPy.load(str(next(bundles.glob('*-ancientloft_nodegraph_assets_all*'))))
ground = None
for obj in env.objects:
    if obj.type.name != 'MonoBehaviour':
        continue
    data = obj.read_typetree()
    if 'ground' in data.get('m_Name', '').lower() and 'nodes' in data:
        ground = data
        break
assert ground, 'The selected castle map has no installed ground node graph.'

center = (-70.375, 40.149, -.975)
points = [tuple(node['position'][axis] for axis in ('x', 'y', 'z'))
          for node in ground['nodes'] if not node['forbiddenHulls'] & 1 and node['gateIndex'] == 0]
heights = []
missing = 0
for dx in range(-28, 29, 7):
    for dz in range(-28, 29, 7):
        x, z = center[0] + dx, center[2] + dz
        local = [point for point in points if abs(point[1] - center[1]) < 8]
        nearest = min(local, key=lambda point: (point[0] - x) ** 2 + (point[2] - z) ** 2)
        if (nearest[0] - x) ** 2 + (nearest[2] - z) ** 2 > 36:
            missing += 1
        else:
            heights.append(nearest[1])
assert len(heights) >= 73, 'The castle plaza lacks ground navigation coverage across its 56m footprint.'
height_range = max(heights) - min(heights)
assert height_range < 1.1, 'The chosen castle plaza is no longer nearly level in the installed map.'
# Check directed ground routes to the keep from the farther spawn ring.
# Navigation data cannot verify live AI movement or physics obstruction.
route_counts = {}
for hull, name in ((1, 'human'), (2, 'golem'), (4, 'beetleQueen')):
    nodes = ground['nodes']
    allowed = {i for i, node in enumerate(nodes)
               if not node['forbiddenHulls'] & hull and node['gateIndex'] == 0}
    def distance(index):
        point = nodes[index]['position']
        return sum((point[axis] - center[i]) ** 2 for i, axis in enumerate(('x', 'y', 'z')))
    start = min(allowed, key=distance)
    incoming = [[] for _ in nodes]
    for link in ground['links']:
        a, b = link['nodeIndexA']['nodeIndex'], link['nodeIndexB']['nodeIndex']
        if a in allowed and b in allowed and link['gateIndex'] == 0 and link['hullMask'] & hull:
            incoming[b].append(a)
    visited, queue = {start}, deque([start])
    while queue:
        for index in incoming[queue.popleft()]:
            if index not in visited:
                visited.add(index)
                queue.append(index)
    ring = [index for index in visited if 60 ** 2 <= distance(index) <= 95 ** 2]
    assert ring, f'No {name} ground route connects the farther spawn ring to the castle.'
    route_counts[name] = len(ring)

# Check real shader properties rather than relying on an unsupported legacy name.
shader_env = UnityPy.load(str(next(bundles.glob('ror2-base-shaders_shader_assets_all*'))))
shader_props = None
for obj in shader_env.objects:
    if obj.type.name != 'Shader':
        continue
    data = obj.read_typetree()
    parsed = data.get('m_ParsedForm', {})
    if parsed.get('m_Name') == 'Hopoo Games/Deferred/Standard':
        shader_props = {prop['m_Name'] for prop in parsed['m_PropInfo']['m_Props']}
        break
assert shader_props, 'The installed native standard shader could not be inspected.'
for required in ('_Color', '_MainTex', '_EmTex', '_EmColor', '_EmPower'):
    assert required in shader_props, 'Required native castle shader property missing: ' + required

# Native camera masks exclude physics-only layers: renderer/collider separation
# is required even when mesh/material construction has no runtime exception.
globals_env = UnityPy.load(str(bundles.parents[2] / 'globalgamemanagers'))
layers = next(obj.read_typetree()['layers'] for obj in globals_env.objects if obj.type.name == 'TagManager')
camera_env = UnityPy.load(str(next(bundles.glob('ror2-base-core-camera_assets_all*'))))
scene_masks = [obj.read_typetree()['m_CullingMask']['m_Bits'] for obj in camera_env.objects
               if obj.type.name == 'Camera' and obj.read().m_GameObject.read().m_Name == 'Scene Camera']
assert scene_masks, 'The native gameplay camera was not inspected.'
for mask in scene_masks:
    assert mask & (1 << layers.index('Default')), 'Default castle render layer is hidden.'
    assert mask & (1 << layers.index('World')), 'World causeway render layer is hidden.'
    assert not mask & (1 << layers.index('CollideWithCharacterHullOnly')), 'Review separation if camera now renders hull-only objects.'

receipt = {'scene': 'ancientloft', 'name': 'Aphelian Sanctuary', 'preferredAnchor': center,
           'sampledFootprintMetres': 56, 'groundSamples': len(heights), 'totalSamples': 81,
           'navigationHeightRangeMetres': round(height_range, 4),
           'spawnRingMetres': [60, 95], 'spawnRingNodesWithRouteToKeep': route_counts,
           'nativeStandardMaterialPropertiesVerified': True, 'nativeGameplayCameraMasksVerified': True, 'gameLaunched': False}
(root / '.build/castle-asset-check.json').write_text(json.dumps(receipt, indent=2), encoding='utf-8')
print(f'PASS: installed castle map covers {len(heights)}/81 footprint samples with {height_range:.2f}m navigation-height variation; connected 60-95m spawn nodes {route_counts}; native material properties verified. No game launched.')
