using UnityEngine;

namespace LoGa.LudoEngine.Utilities
{
    [ExecuteInEditMode]
    public class CircleSpriteGenerator : MonoBehaviour
    {
        [ContextMenu("Generate Circle Sprites")]
        void GenerateCircles()
        {
            // Creates circle textures for waves
            int size = 256;
            int thickness = 8;

            for (int i = 1; i <= 4; i++)
            {
                Texture2D tex = new Texture2D(size, size);
                Color[] pixels = new Color[size * size];

                Vector2 center = new Vector2(size / 2f, size / 2f);
                float radius = (size / 2f) - thickness;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), center);

                        if (dist >= radius && dist <= radius + thickness)
                            pixels[y * size + x] = Color.white;
                        else
                            pixels[y * size + x] = Color.clear;
                    }
                }

                tex.SetPixels(pixels);
                tex.Apply();

                byte[] bytes = tex.EncodeToPNG();
                System.IO.File.WriteAllBytes($"Assets/Sprites/Circle{i}.png", bytes);
            }
        }
    }
}
