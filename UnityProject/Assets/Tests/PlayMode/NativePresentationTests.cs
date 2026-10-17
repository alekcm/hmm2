using System;
using System.IO;
using HoMM5.Core;
using HoMM5.Data;
using HoMM5.Map;
using HoMM5.UI;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HoMM5.Tests
{
    /// <summary>CPU layout/geometry regressions; screenshot matching is a separate manual check.</summary>
    public class NativePresentationTests
    {
        string _previousRoot;
        string _temporaryRoot;

        [SetUp]
        public void Setup()
        {
            _previousRoot = H5Paths.Root;
            _temporaryRoot = Path.Combine(Path.GetTempPath(), "H5Presentation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, "Terrain", "Synthetic"));
            H5Paths.SetOverride(_temporaryRoot);
        }

        [TearDown]
        public void Teardown()
        {
            H5Paths.SetOverride(_previousRoot);
            Directory.Delete(_temporaryRoot, true);
        }

        [Test]
        public void CommandDialHighEndPlacementUsesRightBottomPivot()
        {
            var go = new GameObject("NativeButton", typeof(RectTransform));
            try
            {
                var rect = go.GetComponent<RectTransform>();
                H5UiSkin.Place(rect, new H5UiSkin.Window
                {
                    x = -81, y = -71, width = 82, height = 82,
                    horizontal = "EPA_HIGH_END", vertical = "EPA_HIGH_END"
                });
                Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(1, 0)));
                Assert.That(rect.anchorMax, Is.EqualTo(rect.anchorMin));
                Assert.That(rect.pivot, Is.EqualTo(new Vector2(1, 0)));
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(-81, 71)));
                Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(82, 82)));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ResourcesUseTopCentreAndNativeSize()
        {
            var go = new GameObject("Resources", typeof(RectTransform));
            try
            {
                var rect = go.GetComponent<RectTransform>();
                H5UiSkin.Place(rect, new H5UiSkin.Window
                {
                    x = 1, y = 4, width = 697, height = 52,
                    horizontal = "ERA_CENTER", vertical = "EPA_LOW_END"
                });
                Assert.That(rect.pivot, Is.EqualTo(new Vector2(0.5f, 1)));
                Assert.That(rect.anchorMin, Is.EqualTo(rect.pivot));
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(1, -4)));
                Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(697, 52)));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void MixedGameplayFlagsDoNotGenerateSealedTerrainBoxes()
        {
            var terrain = Terrain(new[] { 2f, 3f, 4f, 8f }, 2,
                new[] { H5Terrain.FlagWater, H5Terrain.FlagGround, H5Terrain.FlagPlateau, H5Terrain.FlagRampBit });
            var mesh = new TerrainMeshBuilder(terrain).BuildChunk(0, 0);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(4));
                Assert.That(mesh.triangles.Length, Is.EqualTo(6));
                Assert.That(mesh.vertices[0].y, Is.EqualTo(2));
                Assert.That(mesh.vertices[1].y, Is.EqualTo(3));
                Assert.That(mesh.vertices[2].y, Is.EqualTo(8));
                Assert.That(mesh.vertices[3].y, Is.EqualTo(4));
                Assert.That(Vector3.Dot(mesh.normals[0], Vector3.up), Is.LessThan(0.99f));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [TestCase(0.75f, 0.25f, 4f)]
        [TestCase(0.25f, 0.75f, 4.5f)]
        [TestCase(0.5f, 0.5f, 5f)]
        [TestCase(1f, 1f, 8f)]
        public void HeightSamplingMatchesMeshTriangles(float x, float y, float expected)
        {
            var terrain = Terrain(new[] { 2f, 3f, 4f, 8f }, 2, new byte[] { 0, 16, 32, 8 });
            Assert.That(terrain.SampleWorldHeight(x, y), Is.EqualTo(expected).Within(0.00001f));
        }

        [Test]
        public void AdjacentChunksHaveIdenticalNormalsAtSharedVertices()
        {
            var terrain = Terrain(new[] { 0f, 1f, 4f, 2f, 3f, 5f, 3f, 6f, 8f }, 3, new byte[9]);
            var builder = new TerrainMeshBuilder(terrain, 1, true);
            var left = builder.BuildChunk(0, 0);
            var right = builder.BuildChunk(1, 0);
            try
            {
                Assert.That(left.vertices[1], Is.EqualTo(right.vertices[0]));
                Assert.That(left.normals[1], Is.EqualTo(right.normals[0]));
                Assert.That(left.vertices[2], Is.EqualTo(right.vertices[3]));
                Assert.That(left.normals[2], Is.EqualTo(right.normals[3]));
                Assert.That(left.subMeshCount, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(left); Object.DestroyImmediate(right); }
        }

        static H5Terrain Terrain(float[] heights, int vertices, byte[] flags)
        {
            var info = new TerrainInfo { tileX = vertices - 1, tileY = vertices - 1, vertexCount = vertices };
            var bytes = new byte[heights.Length * sizeof(float)];
            Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
            File.WriteAllText(H5Paths.Terrain("Synthetic", "GroundTerrain.json"), JsonConvert.SerializeObject(info));
            File.WriteAllBytes(H5Paths.Terrain("Synthetic", "GroundTerrain.heights"), bytes);
            File.WriteAllBytes(H5Paths.Terrain("Synthetic", "GroundTerrain.flags"), flags);
            return H5Terrain.Load("Synthetic", "GroundTerrain");
        }
    }
}
