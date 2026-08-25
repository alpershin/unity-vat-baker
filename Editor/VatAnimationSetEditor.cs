using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Alpershin.Vat.EditorTools
{
    [CustomEditor(typeof(VatAnimationSet))]
    internal sealed class VatAnimationSetEditor : UnityEditor.Editor
    {
        private readonly List<string> _importNotes = new List<string>();
        private RuntimeAnimatorController _controller;
        private int _controllerLayer;

        private static readonly string[] AfterClips =
        {
            "_fps", "_bakeNormals", "_compactPositionMap", "_perUnitAnimation",
            "_lodPrefabs", "_lodTransitions",
            "_createPrefab", "_castShadows", "_shader", "_materialTemplate"
        };

        public override void OnInspectorGUI()
        {
            var set = (VatAnimationSet)target;

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_sourcePrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_sourceClips"), true);
            DrawClipDropArea(set);
            DrawControllerImport(set);

            for (var i = 0; i < AfterClips.Length; i++)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(AfterClips[i]), true);
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bake", GUILayout.Height(28f)))
                {
                    Bake(set);
                }

                if (GUILayout.Button("Preview", GUILayout.Height(28f), GUILayout.Width(90f)))
                {
                    VatPreviewWindow.Open(set);
                }
            }

            DrawSummary(set);
        }

        private static void Bake(VatAnimationSet set)
        {
            var result = VatBaker.Bake(set);
            if (result.IsSuccess)
            {
                Debug.Log(result.Message, set);
                return;
            }

            Debug.LogError(result.Message, set);
        }

        /// <summary>
        /// Dropping the animation FBXs straight in beats picking clips out of them one by one —
        /// a model file is expanded into every clip it holds.
        /// </summary>
        private void DrawClipDropArea(VatAnimationSet set)
        {
            var area = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            GUI.Box(area, "Drop animation clips or model files here");

            var current = Event.current;
            if (!area.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                current.Use();
                return;
            }

            if (current.type != EventType.DragPerform)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            AppendClips(set, CollectDroppedClips());
            current.Use();
        }

        private static List<AnimationClip> CollectDroppedClips()
        {
            var found = new List<AnimationClip>();
            var dropped = DragAndDrop.objectReferences;

            for (var i = 0; i < dropped.Length; i++)
            {
                if (dropped[i] is AnimationClip clip)
                {
                    found.Add(clip);
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(dropped[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                for (var j = 0; j < assets.Length; j++)
                {
                    // Model importers add a preview clip that is not meant to be played.
                    if (assets[j] is AnimationClip inner && !inner.name.StartsWith("__preview__"))
                    {
                        found.Add(inner);
                    }
                }
            }

            return found;
        }

        private void AppendClips(VatAnimationSet set, List<AnimationClip> clips)
        {
            if (clips.Count == 0)
            {
                return;
            }

            var merged = new List<VatSourceClip>(set.SourceClips);
            for (var i = 0; i < clips.Count; i++)
            {
                if (!Contains(merged, clips[i]))
                {
                    merged.Add(new VatSourceClip(clips[i]));
                }
            }

            Apply(set, merged, "Add VAT Clips");
        }

        private static bool Contains(List<VatSourceClip> records, AnimationClip clip)
        {
            for (var i = 0; i < records.Count; i++)
            {
                if (records[i] != null && records[i].Clip == clip)
                {
                    return true;
                }
            }

            return false;
        }

        private void Apply(VatAnimationSet set, List<VatSourceClip> clips, string undoName)
        {
            Undo.RecordObject(set, undoName);
            set.SetSourceClips(clips.ToArray());
            EditorUtility.SetDirty(set);
            serializedObject.Update();
        }

        /// <summary>
        /// Pulls the clip library out of an existing controller. The state machine does not come
        /// along — what it decided has to be re-expressed as calls to Play from your own code.
        /// </summary>
        private void DrawControllerImport(VatAnimationSet set)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Import from Animator", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _controller = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                    _controller, typeof(RuntimeAnimatorController), false);
                _controllerLayer = EditorGUILayout.IntField("Layer", _controllerLayer, GUILayout.Width(120f));

                using (new EditorGUI.DisabledScope(_controller == null))
                {
                    if (GUILayout.Button("Import", GUILayout.Width(80f)))
                    {
                        Import(set);
                    }
                }
            }

            for (var i = 0; i < _importNotes.Count; i++)
            {
                EditorGUILayout.HelpBox(_importNotes[i], MessageType.Warning);
            }
        }

        private void Import(VatAnimationSet set)
        {
            _importNotes.Clear();

            if (!(_controller is AnimatorController controller))
            {
                _importNotes.Add("Only an AnimatorController asset can be read; override controllers are not supported.");
                return;
            }

            var skipped = new List<string>();
            var clips = AnimatorControllerConverter.Convert(controller, _controllerLayer, skipped);

            if (clips.Count == 0)
            {
                _importNotes.Add("No states with a plain animation clip were found on that layer.");
            }
            else
            {
                Apply(set, clips, "Import VAT Clips");
                _importNotes.Add($"Imported {clips.Count} clip(s). Transitions, parameters and conditions are not carried over — drive them from code.");
            }

            for (var i = 0; i < skipped.Count; i++)
            {
                _importNotes.Add($"Skipped {skipped[i]}");
            }
        }

        private static void DrawSummary(VatAnimationSet set)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Baked", EditorStyles.boldLabel);

            if (set.Mesh == null)
            {
                EditorGUILayout.HelpBox("Nothing baked yet.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("LOD levels", set.LodLevels.ToString());
            EditorGUILayout.LabelField("Vertices (LOD0)", set.VertexCount.ToString());
            EditorGUILayout.LabelField("Frames", set.TotalFrames.ToString());
            EditorGUILayout.LabelField("Maps (LOD0)", $"{MegabytesOfMaps(set):F1} MB");

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Prefab", set.Prefab, typeof(GameObject), false);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clips", EditorStyles.boldLabel);
            for (var i = 0; i < set.ClipCount; i++)
            {
                var clip = set.GetClip(i);
                var loop = clip.Looping ? "loop" : "once";
                EditorGUILayout.LabelField(
                    $"{i}. {clip.Name}",
                    $"{clip.FrameCount} frames @ {clip.Fps:F0} fps, {loop}");
            }
        }

        private static float MegabytesOfMaps(VatAnimationSet set)
        {
            const int bytesPerTexel = 8;
            var bytes = 0L;

            if (set.PositionMap != null)
            {
                bytes += (long)set.PositionMap.width * set.PositionMap.height * bytesPerTexel;
            }

            if (set.NormalMap != null)
            {
                bytes += (long)set.NormalMap.width * set.NormalMap.height * bytesPerTexel;
            }

            return bytes / (1024f * 1024f);
        }
    }
}
