using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimpleIPScanner.Models
{
    /// <summary>
    /// Represents a single subnet CIDR to be included in a multi-subnet scan.
    /// Bindable so the chip list updates live when IsSelected changes.
    /// </summary>
    public class SubnetEntry : INotifyPropertyChanged
    {
        private string _cidr = "";
        private string _label = "";
        private bool _isSelected = true;

        /// <summary>The CIDR notation, e.g. "192.168.1.0/24".</summary>
        public string Cidr
        {
            get => _cidr;
            set { _cidr = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Optional friendly label shown alongside the CIDR in the UI chip.
        /// Auto-detected subnets carry the NIC name (e.g. "Wi-Fi"); manually
        /// added ones leave this empty.
        /// </summary>
        public string Label
        {
            get => _label;
            set { _label = value; OnPropertyChanged(); }
        }

        /// <summary>Whether this subnet is included in the next scan.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
