using System;
using System.Globalization;
using System.Windows.Data;
using SqlPilot.Core.Database;

namespace SqlPilot.UI.Converters
{
    public class ObjectTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DatabaseObjectType objType)
            {
                return objType switch
                {
                    DatabaseObjectType.Table => "\uE8A5",              // Table icon
                    DatabaseObjectType.View => "\uE7B3",               // View icon
                    DatabaseObjectType.StoredProcedure => "\uE943",     // Code icon
                    DatabaseObjectType.ScalarFunction => "\uE8EF",     // Function icon
                    DatabaseObjectType.TableValuedFunction => "\uE8EF",
                    DatabaseObjectType.Synonym => "\uE71B",            // Link icon
                    DatabaseObjectType.Schema => "\uE8B7",             // Folder icon
                    _ => "\uE7C3"                                      // Default
                };
            }
            return "\uE7C3";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
