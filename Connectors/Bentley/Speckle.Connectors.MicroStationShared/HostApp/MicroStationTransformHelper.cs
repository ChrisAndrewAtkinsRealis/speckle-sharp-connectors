using Bentley;
using Bentley.DgnPlatformNET;
using Bentley.GeometryNET;
using Speckle.DoubleNumerics;
using Speckle.Sdk;

namespace Speckle.Connectors.MicroStation.HostApp;

/// <summary>
/// Converts MicroStation cell placement transforms to/from Speckle instance transforms.
/// </summary>
/// <remarks>
/// Speckle <see cref="Matrix4x4"/> instance transforms carry translation in the model's master units, while
/// native <see cref="DTransform3d"/> translations are in UoRs — hence the divide/multiply by <c>UorPerMaster</c>.
/// A native transform is a 3x4 affine (rows 0-2, columns 0-3, column 3 = translation), read here via its indexer.
///
/// NOTE: this file is the single most likely surface to need adjustment against a live MicroStation SDK build.
/// The <see cref="DTransform3d"/> indexer read and the <see cref="ToNativeTransform"/> re-composition are
/// isolated here so any fix stays local.
/// </remarks>
public static class MicroStationTransformHelper
{
  /// <summary>
  /// Extracts a cell element's placement transform by listening to the graphics processor's transform
  /// announcement (a callback the v2 connector relied on). Returns identity when none is announced.
  /// </summary>
  public static DTransform3d GetElementTransform(BDE.Element element)
  {
    var processor = new TransformCaptureProcessor();
    try
    {
      ElementGraphicsOutput.Process(element, processor);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // fall back to identity below
    }

    return processor.Transform ?? DTransform3d.Identity;
  }

  public static Matrix4x4 ToInstanceMatrix(DTransform3d t, double uorPerMaster) =>
    new(
      t[0, 0],
      t[0, 1],
      t[0, 2],
      t[0, 3] / uorPerMaster,
      t[1, 0],
      t[1, 1],
      t[1, 2],
      t[1, 3] / uorPerMaster,
      t[2, 0],
      t[2, 1],
      t[2, 2],
      t[2, 3] / uorPerMaster,
      0,
      0,
      0,
      1
    );

  public static DTransform3d ToNativeTransform(Matrix4x4 m, double translationScaleToUor)
  {
    var rotation = new DMatrix3d(m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33);
    var translation = new DPoint3d(
      m.M14 * translationScaleToUor,
      m.M24 * translationScaleToUor,
      m.M34 * translationScaleToUor
    );

    return new DTransform3d(rotation, translation);
  }

  /// <summary>
  /// Captures the transform announced while processing an element's graphics. Overrides mirror the set the
  /// v2 connector implemented so the abstract members are satisfied; we short-circuit all geometry processing
  /// since only the transform is wanted.
  /// </summary>
  private sealed class TransformCaptureProcessor : ElementGraphicsProcessor
  {
    public DTransform3d? Transform { get; private set; }

    public override void AnnounceTransform(DTransform3d trans) => Transform = trans;

    public override bool ProcessAsBody(bool isCurved) => false;

    public override bool ProcessAsFacets(bool isPolyface) => false;

    public override bool WantClipping() => false;

    public override BentleyStatus ProcessSurface(MSBsplineSurface surface) => BentleyStatus.Error;

    public override BentleyStatus ProcessFacets(PolyfaceHeader meshData, bool filled) => BentleyStatus.Error;

    public override BentleyStatus ProcessCurveVector(CurveVector vector, bool isFilled) => BentleyStatus.Success;

    public override BentleyStatus ProcessCurvePrimitive(
      CurvePrimitive curvePrimitive,
      bool isClosed,
      bool isFilled
    ) => BentleyStatus.Success;
  }
}
