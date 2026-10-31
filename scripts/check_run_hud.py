"""Validate the real native HUD hierarchy required by Run, late joins and timer extensions.

The reported WolfoQoL crash was a null Run.InstantiateUi result inside HUD.Awake.
Wolfo 5.2.3 also expects child 0 (TimerPanel), then that panel's child 1 (TimerText).
Inspect installed game assets; this is not a live Unity rendering test.
"""
from pathlib import Path
import json
import sys

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root / '.build/python'))
import UnityPy

bundles = Path('X:/SteamLibrary/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64')
files = [next(bundles.glob('ror2-base-classicrun_' + kind + '_all*')) for kind in ('assets', 'static_assets')]
env = UnityPy.load(*(str(p) for p in files))
objects = {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject' and o.read().m_Name in ('ClassicRun', 'ClassicRunInfoHudPanel')}
hud = objects['ClassicRunInfoHudPanel']

def transform(gameobject):
    return next(c.component.read() for c in gameobject.read().m_Component if c.component.deref().type.name in ('Transform', 'RectTransform'))

native_run = objects['ClassicRun']
run_data = next(c.component.read_typetree() for c in native_run.read().m_Component if c.component.deref().type.name == 'MonoBehaviour' and 'uiPrefab' in c.component.read_typetree())
assert run_data['uiPrefab']['m_FileID'] == 0 and run_data['uiPrefab']['m_PathID'] == hud.path_id, 'Use the same UI prefab as the native normal run.'
children = transform(hud).m_Children
assert len(children) > 0, 'A non-null but childless UI still crashes timer extensions.'
timer_panel = children[0].read()
assert timer_panel.m_GameObject.read().m_Name == 'TimerPanel'
assert len(timer_panel.m_Children) >= 2
timer = timer_panel.m_Children[1].read().m_GameObject
assert timer.read().m_Name == 'TimerText'
timer_components = [c.component.read_typetree() for c in timer.read().m_Component if c.component.deref().type.name == 'MonoBehaviour']
assert any('_seconds' in c and '_format' in c and 'targetLabel' in c for c in timer_components), 'Native TimerText component must be preserved.'
assert any('runStopwatchTimerTextController' in c for c in timer_components), 'Keep the native run timer controller beside its text.'
assert any('m_text' in c for c in timer_components), 'Native timer text graphic must be preserved.'
source = (root / 'src/GauntletMode.cs').read_text(encoding='utf-8')
assert 'run.uiPrefab = FortressAssets.Load<GameObject>("ClassicRunInfoHudPanel_prefab");' in source
assert 'run.uiPrefab = null' not in source
wave_source = (root / 'src/GauntletWaves.cs').read_text(encoding='utf-8')
assert 'NativeAccess.Set(wave, "uiPrefab", null)' in wave_source, 'Keep duplicate Simulacrum wave panels suppressed separately from the run HUD.'
result = {'nativeUi': 'ClassicRunInfoHudPanel', 'rootChildren': [t.read().m_GameObject.read().m_Name for t in children],
          'timerChildren': [t.read().m_GameObject.read().m_Name for t in timer_panel.m_Children],
          'runUiNonNull': True, 'nativeTimerHierarchy': True, 'wavePanelsSuppressed': True, 'unityRuntimeTested': False}
out = root / '.build/hud-audit'
out.mkdir(parents=True, exist_ok=True)
(out / 'native-hud.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print('PASS: native run HUD has the TimerPanel/TimerText hierarchy; game-mode UI is non-null and duplicate wave panels stay suppressed. No Unity runtime loaded.')
