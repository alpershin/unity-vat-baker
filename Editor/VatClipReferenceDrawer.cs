using UnityEditor;
using UnityEngine;

namespace Alpershin.Vat.EditorTools
{
    [CustomPropertyDrawer(typeof(VatClipReference))]
    internal sealed class VatClipReferenceDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing * 3f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var setProperty = property.FindPropertyRelative("_set");
            var nameProperty = property.FindPropertyRelative("_clipName");

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(line, setProperty, label);

            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var set = setProperty.objectReferenceValue as VatAnimationSet;

            if (set == null || set.ClipCount == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.LabelField(line, " ", "Assign a baked set to pick a clip");
                }

                return;
            }

            var names = new string[set.ClipCount];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = set.GetClip(i).Name;
            }

            var current = Mathf.Max(set.IndexOf(nameProperty.stringValue), 0);
            var picked = EditorGUI.Popup(line, " ", current, names);
            if (picked != current || string.IsNullOrEmpty(nameProperty.stringValue))
            {
                nameProperty.stringValue = names[picked];
            }
        }
    }
}
