using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PieceController))]
[CanEditMultipleObjects]
public class PiecePositionHelper : Editor
{
    public override void OnInspectorGUI()
    {
        // Hiển thị Inspector mặc định (để chỉnh PieceType/IsRed nếu cần)
        DrawDefaultInspector();

        var piece = (PieceController)target;
        if (piece == null) return;

        // Tính Mesh Center World theo renderer (nếu có)
        Vector3 meshCenterWorld = piece.transform.position;
        var renderer = piece.GetComponent<Renderer>();
        if (renderer == null) renderer = piece.GetComponentInChildren<Renderer>();
        if (renderer != null) meshCenterWorld = renderer.bounds.center;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("=== Position (read-only) ===", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Mesh Center World:",
            $"X: {meshCenterWorld.x:F3}, Y: {meshCenterWorld.y:F3}, Z: {meshCenterWorld.z:F3}");
        EditorGUILayout.LabelField("File:", piece.file.ToString());
        EditorGUILayout.LabelField("Rank:", piece.rank.ToString());
    }
}

