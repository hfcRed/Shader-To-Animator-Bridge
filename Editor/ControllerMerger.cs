using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace hfcRed.ShaderToAnimatorBridge
{
    public static class ControllerMerger
    {
        public static void CopyLayers(AnimatorController src, AnimatorController dst)
        {
            foreach (var p in src.parameters)
                if (!dst.parameters.Any(x => x.name == p.name))
                    dst.AddParameter(new AnimatorControllerParameter { name = p.name, type = p.type, defaultFloat = p.defaultFloat, defaultInt = p.defaultInt, defaultBool = p.defaultBool });

            string path = AssetDatabase.GetAssetPath(dst);
            var trees = new Dictionary<BlendTree, BlendTree>();
            foreach (var layer in src.layers)
            {
                dst.AddLayer(new AnimatorControllerLayer
                {
                    name = layer.name,
                    defaultWeight = layer.defaultWeight,
                    blendingMode = layer.blendingMode,
                    avatarMask = layer.avatarMask,
                    iKPass = layer.iKPass,
                    stateMachine = new MachineCopy(path, trees).Copy(layer.stateMachine),
                });
            }
        }

        public static void Remove(AnimatorController c, BridgeSettings s, HashSet<string> layers)
        {
            for (int i = c.layers.Length - 1; i >= 0; i--)
                if (layers.Contains(c.layers[i].name)) c.RemoveLayer(i);

            foreach (var p in c.parameters)
                if (p.name.StartsWith(s.ParameterPrefix) || (s.sync && p.name == s.SyncParameter)) c.RemoveParameter(p);

            var used = new HashSet<Motion>();
            void Collect(Motion m)
            {
                if (m == null || !used.Add(m)) return;
                if (m is BlendTree bt) foreach (var child in bt.children) Collect(child.motion);
            }

            void CollectMachine(AnimatorStateMachine sm)
            {
                foreach (var st in sm.states) Collect(st.state.motion);
                foreach (var sub in sm.stateMachines) CollectMachine(sub.stateMachine);
            }

            foreach (var layer in c.layers) CollectMachine(layer.stateMachine);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(c)))
                if (o is BlendTree tree && tree.name.StartsWith(s.LayerPrefix) && !used.Contains(tree)) Object.DestroyImmediate(tree, true);
        }

        class MachineCopy
        {
            readonly string path;
            readonly Dictionary<BlendTree, BlendTree> trees;
            readonly Dictionary<AnimatorState, AnimatorState> states = new Dictionary<AnimatorState, AnimatorState>();
            readonly Dictionary<AnimatorStateMachine, AnimatorStateMachine> machines = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();

            public MachineCopy(string path, Dictionary<BlendTree, BlendTree> trees)
            {
                this.path = path;
                this.trees = trees;
            }

            public AnimatorStateMachine Copy(AnimatorStateMachine root)
            {
                var copy = CopyStructure(root);
                foreach (var pair in machines) CopyTransitions(pair.Key, pair.Value);
                return copy;
            }

            AnimatorStateMachine CopyStructure(AnimatorStateMachine sm)
            {
                var copy = new AnimatorStateMachine
                {
                    name = sm.name,
                    hideFlags = HideFlags.HideInHierarchy,
                    anyStatePosition = sm.anyStatePosition,
                    entryPosition = sm.entryPosition,
                    exitPosition = sm.exitPosition,
                    parentStateMachinePosition = sm.parentStateMachinePosition,
                };

                AssetDatabase.AddObjectToAsset(copy, path);
                machines[sm] = copy;
                foreach (var child in sm.states)
                {
                    var st = child.state;
                    var c = new AnimatorState
                    {
                        name = st.name,
                        hideFlags = HideFlags.HideInHierarchy,
                        motion = CopyMotion(st.motion),
                        speed = st.speed,
                        speedParameter = st.speedParameter,
                        speedParameterActive = st.speedParameterActive,
                        cycleOffset = st.cycleOffset,
                        cycleOffsetParameter = st.cycleOffsetParameter,
                        cycleOffsetParameterActive = st.cycleOffsetParameterActive,
                        mirror = st.mirror,
                        mirrorParameter = st.mirrorParameter,
                        mirrorParameterActive = st.mirrorParameterActive,
                        timeParameter = st.timeParameter,
                        timeParameterActive = st.timeParameterActive,
                        iKOnFeet = st.iKOnFeet,
                        writeDefaultValues = st.writeDefaultValues,
                        tag = st.tag,
                    };

                    AssetDatabase.AddObjectToAsset(c, path);
                    copy.AddState(c, child.position);
                    foreach (var b in st.behaviours) EditorUtility.CopySerialized(b, c.AddStateMachineBehaviour(b.GetType()));
                    states[st] = c;
                }

                foreach (var child in sm.stateMachines) copy.AddStateMachine(CopyStructure(child.stateMachine), child.position);
                foreach (var b in sm.behaviours) EditorUtility.CopySerialized(b, copy.AddStateMachineBehaviour(b.GetType()));

                if (sm.defaultState != null) copy.defaultState = states[sm.defaultState];
                return copy;
            }

            void CopyTransitions(AnimatorStateMachine sm, AnimatorStateMachine copy)
            {
                foreach (var child in sm.states)
                    foreach (var t in child.state.transitions)
                    {
                        var c = states[child.state];
                        var nt = t.isExit ? c.AddExitTransition() : t.destinationState != null ? c.AddTransition(states[t.destinationState]) : c.AddTransition(machines[t.destinationStateMachine]);
                        CopySettings(t, nt);
                    }

                foreach (var t in sm.anyStateTransitions)
                    CopySettings(t, t.destinationState != null ? copy.AddAnyStateTransition(states[t.destinationState]) : copy.AddAnyStateTransition(machines[t.destinationStateMachine]));

                foreach (var t in sm.entryTransitions)
                {
                    var nt = t.destinationState != null ? copy.AddEntryTransition(states[t.destinationState]) : copy.AddEntryTransition(machines[t.destinationStateMachine]);
                    nt.conditions = t.conditions;
                }
            }

            static void CopySettings(AnimatorStateTransition t, AnimatorStateTransition nt)
            {
                nt.name = t.name;
                nt.conditions = t.conditions;
                nt.hasExitTime = t.hasExitTime;
                nt.exitTime = t.exitTime;
                nt.hasFixedDuration = t.hasFixedDuration;
                nt.duration = t.duration;
                nt.offset = t.offset;
                nt.interruptionSource = t.interruptionSource;
                nt.orderedInterruption = t.orderedInterruption;
                nt.canTransitionToSelf = t.canTransitionToSelf;
                nt.mute = t.mute;
                nt.solo = t.solo;
            }

            Motion CopyMotion(Motion m)
            {
                if (!(m is BlendTree bt)) return m;
                if (trees.TryGetValue(bt, out var done)) return done;

                var copy = new BlendTree();
                EditorUtility.CopySerialized(bt, copy);
                copy.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(copy, path);
                trees[bt] = copy;
                var children = copy.children;

                for (int i = 0; i < children.Length; i++) children[i].motion = CopyMotion(children[i].motion);
                copy.children = children;
                return copy;
            }
        }
    }
}
