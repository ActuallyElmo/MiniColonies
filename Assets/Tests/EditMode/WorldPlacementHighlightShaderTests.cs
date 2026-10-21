using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

// Render the actual shader at explicit times; no scene, frame waits or duplicate CPU mask.
public sealed class WorldPlacementHighlightShaderTests
{
    private const int Size = 12;
    private Material material;
    private Mesh quad;
    private RenderTexture target;
    private Texture2D pixels;
    private CommandBuffer commands;
    private float previousTime;

    [SetUp]
    public void SetUp()
    {
        previousTime = Shader.GetGlobalFloat("_WorldPlacementHighlightTime");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Shader rendering requires a graphics device.");
        Shader shader = Shader.Find("MiniColonies/MapResourceHighlight");
        Assert.IsNotNull(shader);
        material = new Material(shader);
        material.SetTexture("_MainTex", Texture2D.whiteTexture);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_DashPixels", 3f);
        material.SetFloat("_GapPixels", 3f);
        material.SetFloat("_OutlineWidthPixels", 1f);
        material.SetFloat("_PixelsPerWorldUnit", 6f);
        quad = new Mesh
        {
            vertices = new[] { Vector3.zero, Vector3.forward, Vector3.forward + Vector3.right, Vector3.right },
            uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
            triangles = new[] { 0, 1, 2, 0, 2, 3 }
        };
        target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        pixels = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
        commands = new CommandBuffer { name = "Highlight shader regression" };
    }

    [TearDown]
    public void TearDown()
    {
        commands?.Dispose();
        if (target != null) RenderTexture.ReleaseTemporary(target);
        if (pixels != null) Object.DestroyImmediate(pixels);
        if (quad != null) Object.DestroyImmediate(quad);
        if (material != null) Object.DestroyImmediate(material);
        Shader.SetGlobalFloat("_WorldPlacementHighlightTime", previousTime);
    }

    [TestCase(0f, 8f)]
    [TestCase(1f, 8f)]
    [TestCase(1f, -8f)]
    public void EveryHatchPhaseAppearsOncePerCycleWithoutSkipping(float mode, float speed)
    {
        material.SetFloat("_MotionMode", mode);
        material.SetFloat("_StepsPerSecond", speed);
        var states = new HashSet<string>();
        string first = null;
        string previous = null;
        for (int tick = 0; tick <= 6; tick++)
        {
            string state = Render((tick + 0.25f) / 8f);
            Assert.AreEqual(state, Render((tick + 0.75f) / 8f), "The pattern must hold still between ticks.");
            int lit = 0;
            int changed = 0;
            for (int i = 0; i < state.Length; i++)
            {
                if (state[i] == '1') lit++;
                if (previous != null && state[i] != previous[i]) changed++;
            }
            Assert.AreEqual(Size * Size / 2, lit, "Three lit and three empty pixels give uniform coverage.");
            if (previous != null)
                Assert.AreEqual(Size * Size / 3, changed, "Each tick must advance exactly one of the six hatch phases.");
            if (tick == 0) first = state;
            if (tick < 6) Assert.IsTrue(states.Add(state), "No phase may repeat before all six have appeared.");
            else Assert.AreEqual(first, state, "The seventh state must wrap to the first.");
            previous = state;
        }
    }

    [Test]
    public void HigherDensityAddsPatternPixelsInsideTheSameWorldArea()
    {
        material.SetFloat("_StepsPerSecond", 0f);
        string coarse = Render(0f);
        material.SetFloat("_OutlineWidthPixels", 2f);
        string fine = Render(0f);
        Assert.AreEqual(2, RowTransitions(coarse));
        Assert.AreEqual(4, RowTransitions(fine), "Twice the density must fit twice as many dashes in the same world span.");
    }

    [Test]
    public void SelectionIsSolidAtEveryAnimationPhaseWithoutChangingHoverMaterial()
    {
        material.SetFloat("_SolidOutline", 1f);
        string selected = Render(0f);
        Assert.AreEqual(new string('1', Size * Size), selected);
        for (int tick = 1; tick <= 6; tick++)
            Assert.AreEqual(selected, Render((tick + 0.25f) / 8f));
        material.SetFloat("_SolidOutline", 0f);
        Assert.AreNotEqual(selected, Render(0f), "Hover must still use the animated gaps.");
    }

    private static int RowTransitions(string state)
    {
        int count = 0;
        for (int x = 0; x < Size; x++)
            if (state[x] != state[(x + 1) % Size]) count++;
        return count;
    }

    private string Render(float time)
    {
        // View the XZ unit quad from above. Pixel centers avoid stripe boundaries.
        var view = Matrix4x4.zero;
        view.m00 = 1f; view.m12 = 1f; view.m21 = -1f; view.m23 = -1f; view.m33 = 1f;
        var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0f, 1f, 0f, 1f, 0.1f, 10f), true);
        commands.Clear();
        commands.SetRenderTarget(target);
        commands.SetViewport(new Rect(0, 0, Size, Size));
        commands.ClearRenderTarget(false, true, Color.black);
        commands.SetViewProjectionMatrices(view, projection);
        commands.SetGlobalFloat("_WorldPlacementHighlightTime", time);
        commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
        RenderTexture previousTarget = RenderTexture.active;
        try
        {
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
            Color32[] colors = pixels.GetPixels32();
            var state = new char[colors.Length];
            for (int i = 0; i < state.Length; i++) state[i] = colors[i].r > 127 ? '1' : '0';
            return new string(state);
        }
        finally
        {
            RenderTexture.active = previousTarget;
        }
    }
}
