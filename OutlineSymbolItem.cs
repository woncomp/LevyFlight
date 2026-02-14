using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LevyFlight
{
    /// <summary>
    /// Kinds of symbols shown in the Bird's Eye View outline.
    /// </summary>
    public enum OutlineSymbolKind
    {
        Namespace,
        Class,
        Struct,
        Union,
        Enum,
        EnumMember,
        Function,
        Field,
        Variable,
        Macro,
        TypeDef,
        UsingAlias,
    }

    /// <summary>
    /// Access levels for C++ symbols — used to select the correct icon variant.
    /// </summary>
    public enum AccessLevel
    {
        Public,
        Private,
        Protected,
    }

    /// <summary>
    /// Represents one item in the Bird's Eye View document outline.
    /// Supports hierarchical nesting (namespaces → classes → members)
    /// and WPF data-binding via <see cref="INotifyPropertyChanged"/>.
    /// </summary>
    public class OutlineSymbolItem : INotifyPropertyChanged
    {
        private string _name;
        private bool _isExpanded = true;
        private bool _isSelected;
        private bool _isVisible = true;

        public OutlineSymbolItem(string name, OutlineSymbolKind kind, AccessLevel access = AccessLevel.Public)
        {
            _name = name;
            Kind = kind;
            Access = access;
            Children = new ObservableCollection<OutlineSymbolItem>();
        }

        /// <summary>Display name (e.g. "void foo(int)" or "MyClass").</summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public OutlineSymbolKind Kind { get; set; }
        public AccessLevel Access { get; set; }

        /// <summary>1-based line number where this symbol starts.</summary>
        public int StartLine { get; set; }
        /// <summary>0-based column where this symbol starts.</summary>
        public int StartColumn { get; set; }
        /// <summary>1-based line number where this symbol ends.</summary>
        public int EndLine { get; set; }

        /// <summary>Hierarchical children (e.g. class members).</summary>
        public ObservableCollection<OutlineSymbolItem> Children { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Used by the filter: hidden items are removed from the filtered view.
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Returns the appropriate VS <see cref="ImageMoniker"/> for this symbol.
        /// </summary>
        public ImageMoniker IconMoniker => GetMoniker(Kind, Access);

        /// <summary>
        /// Maps a symbol kind + access level to the matching KnownMonikers icon.
        /// </summary>
        public static ImageMoniker GetMoniker(OutlineSymbolKind kind, AccessLevel access)
        {
            switch (kind)
            {
                case OutlineSymbolKind.Namespace:
                    return KnownMonikers.Namespace;

                case OutlineSymbolKind.Class:
                    switch (access)
                    {
                        case AccessLevel.Private: return KnownMonikers.ClassPrivate;
                        case AccessLevel.Protected: return KnownMonikers.ClassProtected;
                        default: return KnownMonikers.ClassPublic;
                    }

                case OutlineSymbolKind.Struct:
                    switch (access)
                    {
                        case AccessLevel.Private: return KnownMonikers.StructurePrivate;
                        case AccessLevel.Protected: return KnownMonikers.StructureProtected;
                        default: return KnownMonikers.StructurePublic;
                    }

                case OutlineSymbolKind.Union:
                    return KnownMonikers.UnionPublic;

                case OutlineSymbolKind.Enum:
                    switch (access)
                    {
                        case AccessLevel.Private: return KnownMonikers.EnumerationPrivate;
                        case AccessLevel.Protected: return KnownMonikers.EnumerationProtected;
                        default: return KnownMonikers.EnumerationPublic;
                    }

                case OutlineSymbolKind.EnumMember:
                    return KnownMonikers.EnumerationItemPublic;

                case OutlineSymbolKind.Function:
                    switch (access)
                    {
                        case AccessLevel.Private: return KnownMonikers.MethodPrivate;
                        case AccessLevel.Protected: return KnownMonikers.MethodProtected;
                        default: return KnownMonikers.MethodPublic;
                    }

                case OutlineSymbolKind.Field:
                case OutlineSymbolKind.Variable:
                    switch (access)
                    {
                        case AccessLevel.Private: return KnownMonikers.FieldPrivate;
                        case AccessLevel.Protected: return KnownMonikers.FieldProtected;
                        default: return KnownMonikers.FieldPublic;
                    }

                case OutlineSymbolKind.Macro:
                    return KnownMonikers.MacroPublic;

                case OutlineSymbolKind.TypeDef:
                case OutlineSymbolKind.UsingAlias:
                    return KnownMonikers.TypeDefinitionPublic;

                default:
                    return KnownMonikers.FieldPublic;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
