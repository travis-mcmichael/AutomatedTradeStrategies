using System;

namespace AutomatedTrading.Core.Utility;

[AttributeUsage(AttributeTargets.Property)]
public class ColumnNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}