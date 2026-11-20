using System;
using System.Collections.Generic;

#if NO_FORGE_TYPE_ID
namespace Autodesk.Revit.DB
{
    public sealed class ForgeTypeId
    {
        public string TypeId { get; }

        public ForgeTypeId(string typeId)
        {
            TypeId = typeId ?? throw new ArgumentNullException(nameof(typeId));
        }

        public bool Empty()
        {
            return string.IsNullOrEmpty(TypeId);
        }

        public override string ToString()
        {
            return TypeId;
        }

        public override bool Equals(object obj)
        {
            return obj is ForgeTypeId other && string.Equals(TypeId, other.TypeId, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return TypeId?.GetHashCode() ?? 0;
        }
    }

    public static class UnitTypeId
    {
        public static readonly ForgeTypeId General = new ForgeTypeId("General");
        public static readonly ForgeTypeId Meters = new ForgeTypeId("Meters");
        public static readonly ForgeTypeId Millimeters = new ForgeTypeId("Millimeters");
        public static readonly ForgeTypeId Centimeters = new ForgeTypeId("Centimeters");
        public static readonly ForgeTypeId Feet = new ForgeTypeId("Feet");
        public static readonly ForgeTypeId Inches = new ForgeTypeId("Inches");
        public static readonly ForgeTypeId FeetFractionalInches = new ForgeTypeId("FeetFractionalInches");
        public static readonly ForgeTypeId SquareMeters = new ForgeTypeId("SquareMeters");
        public static readonly ForgeTypeId SquareFeet = new ForgeTypeId("SquareFeet");
        public static readonly ForgeTypeId CubicMeters = new ForgeTypeId("CubicMeters");
        public static readonly ForgeTypeId CubicFeet = new ForgeTypeId("CubicFeet");
        public static readonly ForgeTypeId Liters = new ForgeTypeId("Liters");
        public static readonly ForgeTypeId CubicMetersPerHour = new ForgeTypeId("CubicMetersPerHour");
        public static readonly ForgeTypeId LitersPerSecond = new ForgeTypeId("LitersPerSecond");
        public static readonly ForgeTypeId CubicFeetPerMinute = new ForgeTypeId("CubicFeetPerMinute");
        public static readonly ForgeTypeId MetersPerSecond = new ForgeTypeId("MetersPerSecond");
        public static readonly ForgeTypeId FeetPerSecond = new ForgeTypeId("FeetPerSecond");
        public static readonly ForgeTypeId MetersOfWaterColumn = new ForgeTypeId("MetersOfWaterColumn");
        public static readonly ForgeTypeId MillimetersOfWaterColumn = new ForgeTypeId("MillimetersOfWaterColumn");
        public static readonly ForgeTypeId Kilopascals = new ForgeTypeId("Kilopascals");
        public static readonly ForgeTypeId Pascals = new ForgeTypeId("Pascals");
        public static readonly ForgeTypeId Bars = new ForgeTypeId("Bars");
        public static readonly ForgeTypeId KilogramsForcePerSquareMeter = new ForgeTypeId("KilogramsForcePerSquareMeter");
        public static readonly ForgeTypeId MillimetersOfWaterColumnPerMeter = new ForgeTypeId("MillimetersOfWaterColumnPerMeter");
        public static readonly ForgeTypeId MetersOfWaterColumnPerMeter = new ForgeTypeId("MetersOfWaterColumnPerMeter");
        public static readonly ForgeTypeId PascalsPerMeter = new ForgeTypeId("PascalsPerMeter");
        public static readonly ForgeTypeId OneToRatio = new ForgeTypeId("OneToRatio");
        public static readonly ForgeTypeId RatioTo1 = new ForgeTypeId("RatioTo1");
        public static readonly ForgeTypeId Percentage = new ForgeTypeId("Percentage");
        public static readonly ForgeTypeId SlopeDegrees = new ForgeTypeId("SlopeDegrees");
    }

    public static class SpecTypeId
    {
        public static readonly ForgeTypeId Length = new ForgeTypeId("Length");
        public static readonly ForgeTypeId Area = new ForgeTypeId("Area");
        public static readonly ForgeTypeId Volume = new ForgeTypeId("Volume");
        public static readonly ForgeTypeId AirFlow = new ForgeTypeId("AirFlow");
        public static readonly ForgeTypeId PipeSize = new ForgeTypeId("PipeSize");
        public static readonly ForgeTypeId Flow = new ForgeTypeId("Flow");
        public static readonly ForgeTypeId PipingVelocity = new ForgeTypeId("PipingVelocity");
        public static readonly ForgeTypeId PipingPressure = new ForgeTypeId("PipingPressure");
        public static readonly ForgeTypeId PipingFriction = new ForgeTypeId("PipingFriction");
        public static readonly ForgeTypeId PipingSlope = new ForgeTypeId("PipingSlope");
        public static readonly ForgeTypeId Angle = new ForgeTypeId("Angle");
        public static readonly ForgeTypeId Number = new ForgeTypeId("Number");

        public static class String
        {
            public static readonly ForgeTypeId Text = new ForgeTypeId("Text");
        }
    }

    public static class GroupTypeId
    {
        public static readonly ForgeTypeId Geometry = new ForgeTypeId("Geometry");
        public static readonly ForgeTypeId IdentityData = new ForgeTypeId("IdentityData");
        public static readonly ForgeTypeId Data = new ForgeTypeId("Data");
    }
}

namespace System.Linq
{
    public static class EnumerableCompatibility
    {
        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source)
        {
            return new HashSet<TSource>(source);
        }

        public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source, IEqualityComparer<TSource> comparer)
        {
            return new HashSet<TSource>(source, comparer);
        }
    }
}
#endif
