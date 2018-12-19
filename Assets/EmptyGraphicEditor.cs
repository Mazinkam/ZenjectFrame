using UnityEditor;

[CustomEditor(typeof(EmptyGraphic))]
public class EmptyGraphicEditor : Editor
{
	public override void OnInspectorGUI()
	{
		// Nothing to draw
	}
}