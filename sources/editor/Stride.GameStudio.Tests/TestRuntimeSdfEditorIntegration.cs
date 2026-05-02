// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Stride.Assets.SpriteFont;
using Stride.Core.Assets;
using Stride.Graphics;
using Stride.Graphics.Font;
using Xunit;

namespace Stride.GameStudio.Tests
{
    public class TestRuntimeSdfEditorIntegration
    {
        static TestRuntimeSdfEditorIntegration()
        {
            RuntimeHelpers.RunModuleConstructor(typeof(Asset).Module.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(typeof(SpriteFontAsset).Module.ModuleHandle);
        }

        [Fact]
        public void RuntimeSdfFactory_Should_Be_Registered_And_Create_RuntimeSdf_SpriteFontAsset()
        {
            var factory = AssetRegistry.GetAssetFactory(nameof(RuntimeSignedDistanceFieldSpriteFontFactory));

            Assert.NotNull(factory);

            var asset = Assert.IsType<SpriteFontAsset>(factory!.New());
            Assert.IsType<RuntimeSignedDistanceFieldSpriteFontType>(asset.FontType);
        }

        [Fact]
        public void RuntimeSdfThumbnailPreview_Should_Use_DynamicRasterized_Preview_Font()
        {
            var runtimeSdfFontType = typeof(SpriteFont).Assembly.GetType("Stride.Graphics.Font.RuntimeSignedDistanceFieldSpriteFont", throwOnError: true)!;
            var runtimeSdfFont = (SpriteFont)Activator.CreateInstance(runtimeSdfFontType, nonPublic: true)!;

            typeof(SpriteFont).GetProperty(nameof(SpriteFont.Size), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(runtimeSdfFont, 42f);
            runtimeSdfFontType.GetField("FontName", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtimeSdfFont, "Arial");
            runtimeSdfFontType.GetField("Style", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtimeSdfFont, FontStyle.Regular);
            runtimeSdfFontType.GetField("UseKerning", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtimeSdfFont, true);
            runtimeSdfFont.DefaultCharacter = 'A';
            typeof(SpriteFont).GetField("fontSystem", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtimeSdfFont, new FontSystem());

            var commandType = typeof(Stride.Assets.Presentation.Thumbnails.FontThumbnailCompiler).Assembly.GetType("Stride.Assets.Presentation.Thumbnails.FontThumbnailBuildCommand", throwOnError: true)!;
            var selectMethod = commandType.GetMethod("SelectPreviewFont", BindingFlags.Static | BindingFlags.NonPublic)!;
            var selection = selectMethod.Invoke(null, [runtimeSdfFont])!;

            var usingRasterizedPreview = (bool)selection.GetType().GetProperty("UsingRasterizedPreviewForRuntimeSdf")!.GetValue(selection)!;
            var selectedFont = (SpriteFont)selection.GetType().GetProperty("Font")!.GetValue(selection)!;

            Assert.True(usingRasterizedPreview);
            Assert.NotSame(runtimeSdfFont, selectedFont);
            Assert.Equal(SpriteFontType.Dynamic, selectedFont.FontType);
        }
    }
}
