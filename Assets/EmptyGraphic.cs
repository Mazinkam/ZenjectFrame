using UnityEngine;
using UnityEngine.UI;

public class EmptyGraphic : Graphic
{
	private Texture texture;
	private Material renderingMaterial;

	public override Texture mainTexture { get { return this.texture; } }
	public override Material materialForRendering { get { return this.renderingMaterial; } }

	protected override void Awake()
	{
		base.Awake();

		var childGraphics = this.GetComponentsInChildren<Graphic>(true);
		for (int i = 1; i < childGraphics.Length; i++)
		{
			if (childGraphics[i].mainTexture != null)
			{
				this.texture = childGraphics[i].mainTexture;
				this.renderingMaterial = childGraphics[i].materialForRendering;
			}
		}
	}

	protected override void UpdateGeometry()
	{
		// Do nothing
	}
}
