﻿using H.Core.Models.Animals;
using H.Infrastructure;

namespace H.Core.Models.Infrastructure
{
    public class ADManagementPeriodViewItem : ModelBase
    {
        #region Fields

        private bool _isSelected;

        #endregion

        #region Constructors

        public ADManagementPeriodViewItem()
        {
            this.DailyPercentage = 100;
        }

        #endregion

        #region Properties

        public ManagementPeriod ManagementPeriod { get; set; }
        public AnimalComponentBase AnimalComponent { get; set; }
        public AnimalGroup AnimalGroup { get; set; }

        public string ManureStateTypeString { get; set; }
        public string AnimalTypeString { get; set; }
        public string ComponentName { get; set; }

        public bool IsSelected
        {
            get { return _isSelected;}
            set { SetProperty(ref _isSelected, value); }
        }

        public double DailyPercentage { get; set; }

        #endregion
    }
}