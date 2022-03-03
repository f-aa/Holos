﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Permissions;
using H.Core.Emissions;
using H.Core.Emissions.Results;
using H.Core.Enumerations;
using H.Core.Models;
using H.Core.Models.Animals;
using H.Core.Providers.Animals;

namespace H.Core.Services.Animals
{
    public class PoultryResultsService : AnimalResultsServiceBase, IPoultryResultsService
    {
        #region Fields

        //Referenced in  Eqn. 3.5.3-4
        private const double FractionLeaching = 0;
        private PoultryDietInformationProvider_Table_44 _dietInformationProvider = new PoultryDietInformationProvider_Table_44();

        #endregion

        #region Constructors

        public PoultryResultsService() : base()
        {
            _animalComponentCategory = ComponentCategory.Poultry;
        }

        #endregion

        #region Public Methods

        #endregion

        #region Protected Methods

        protected override GroupEmissionsByDay CalculateDailyEmissions(
            AnimalComponentBase animalComponentBase,
            ManagementPeriod managementPeriod,
            DateTime dateTime,
            GroupEmissionsByDay previousDaysEmissions,
            AnimalGroup animalGroup,
            Farm farm)
        {
            var dailyEmissions = new GroupEmissionsByDay();
            var temperature = farm.ClimateData.TemperatureData.GetMeanTemperatureForMonth(dateTime.Month);

            if (animalGroup.GroupType.IsNewlyHatchedEggs() || animalGroup.GroupType.IsEggs())
            {
                return dailyEmissions;
            }

            /*
             * Enteric methane (CH4)
             */

            // Equation 3.4.1-1
            dailyEmissions.EntericMethaneEmission =
                this.CalculateEntericMethaneEmissionForSwinePoultryAndOtherLivestock(
                    entericMethaneEmissionRate: managementPeriod.ManureDetails.YearlyEntericMethaneRate,
                    numberOfAnimals: managementPeriod.NumberOfAnimals);

            /*
             * Manure carbon (C) and methane (CH4)
             */

            // Equation 4.1.1-3
            dailyEmissions.FecalCarbonExcretionRate =
                base.CalculateFecalCarbonExcretionRateForSheepPoultryAndOtherLivestock(
                    manureExcretionRate: managementPeriod.ManureDetails.ManureExcretionRate,
                    carbonFractionOfManure: managementPeriod.ManureDetails.FractionOfCarbonInManure);

            // Equation 4.1.1-4
            dailyEmissions.FecalCarbonExcretion = base.CalculateAmountOfFecalCarbonExcreted(
                excretionRate: dailyEmissions.FecalCarbonExcretionRate,
                numberOfAnimals: managementPeriod.NumberOfAnimals);

            // Equation 4.1.1-5
            dailyEmissions.RateOfCarbonAddedFromBeddingMaterial = base.CalculateRateOfCarbonAddedFromBeddingMaterial(
                beddingRate: managementPeriod.HousingDetails.UserDefinedBeddingRate,
                carbonConcentrationOfBeddingMaterial: managementPeriod.HousingDetails
                    .TotalCarbonKilogramsDryMatterForBedding,
                beddingMaterialType: managementPeriod.HousingDetails.BeddingMaterialType);

            // Equation 4.1.1-6
            dailyEmissions.CarbonAddedFromBeddingMaterial = base.CalculateAmountOfCarbonAddedFromBeddingMaterial(
                rateOfCarbonAddedFromBedding: dailyEmissions.RateOfCarbonAddedFromBeddingMaterial,
                numberOfAnimals: managementPeriod.NumberOfAnimals);

            // Equation 4.1.1-7
            dailyEmissions.CarbonFromManureAndBedding = base.CalculateAmountOfCarbonFromManureAndBedding(
                carbonExcreted: dailyEmissions.FecalCarbonExcretion,
                carbonFromBedding: dailyEmissions.CarbonAddedFromBeddingMaterial);

            if (animalGroup.GroupType.IsChickenType())
            {
                // For chicken, we have VS values used to calculate the manure CH4 emission rate
                dailyEmissions.VolatileSolids = managementPeriod.ManureDetails.VolatileSolids;

                dailyEmissions.ManureMethaneEmissionRate = this.CalculateManureMethaneEmissionRate(
                    volatileSolids: dailyEmissions.VolatileSolids,
                    methaneProducingCapacity: managementPeriod.ManureDetails.MethaneProducingCapacityOfManure,
                    methaneConversionFactor: managementPeriod.ManureDetails.MethaneConversionFactor);
            }
            else
            {
                // For turkeys, we use a constant manure CH4 emission rate
                dailyEmissions.ManureMethaneEmissionRate =
                    managementPeriod.ManureDetails.DailyManureMethaneEmissionRate;
            }

            // Equation 4.1.2-5
            dailyEmissions.ManureMethaneEmission = base.CalculateManureMethane(
                emissionRate: dailyEmissions.ManureMethaneEmissionRate,
                numberOfAnimals: managementPeriod.NumberOfAnimals);

            // Equation 4.1.3-13
            dailyEmissions.AmountOfCarbonLostAsMethaneDuringManagement =
                base.CalculateCarbonLostAsMethaneDuringManagement(
                    monthlyManureMethaneEmission: dailyEmissions.ManureMethaneEmission);

            // Equation 4.1.3-14
            dailyEmissions.AmountOfCarbonInStoredManure = base.CalculateAmountOfCarbonInStoredManure(
                monthlyFecalCarbonExcretion: dailyEmissions.FecalCarbonExcretion,
                monthlyAmountOfCarbonFromBedding: dailyEmissions.CarbonAddedFromBeddingMaterial,
                monthlyAmountOfCarbonLostAsMethaneDuringManagement: dailyEmissions
                    .AmountOfCarbonLostAsMethaneDuringManagement);

            /*
             * Direct manure N2O
             */

            if (managementPeriod.AnimalType.IsTurkeyType())
            {
                /*
                 * Turkeys groups have a constant rate defined by table 44
                 */

                // Equation 4.2.1-24
                dailyEmissions.NitrogenExcretionRate = managementPeriod.ManureDetails.NitrogenExretionRate;
            }
            else
            {
                /*
                 * Chickens have a calculated rate
                 */

                var poultryDietData = _dietInformationProvider.Get(managementPeriod.AnimalType);

                dailyEmissions.DryMatterIntake = poultryDietData.DailyMeanIntake;
                managementPeriod.SelectedDiet.CrudeProtein = poultryDietData.CrudeProtein;

                dailyEmissions.ProteinIntake = this.CalculateProteinIntakePoultry(
                    dryMatterIntake: dailyEmissions.DryMatterIntake,
                    crudeProtein: managementPeriod.SelectedDiet.CrudeProtein);

                if (managementPeriod.AnimalType == AnimalType.LayersDryPoultry ||
                    managementPeriod.AnimalType == AnimalType.LayersWetPoultry)
                {
                    dailyEmissions.ProteinRetained = this.CalculateProteinRetainedLayers(
                        proteinInLiveWeight: poultryDietData.ProteinLiveWeight,
                        weightGain: poultryDietData.WeightGain,
                        proteinContentOfEggs: poultryDietData.ProteinContentEgg,
                        eggProduction: poultryDietData.EggProduction);
                }
                else
                {
                    dailyEmissions.ProteinRetained = this.CalculateProteinRetainedBroilers(
                        initialWeight: poultryDietData.InitialWeight,
                        finalWeight: poultryDietData.FinalWeight,
                        productionPeriod: poultryDietData.ProductionPeriod);
                }

                dailyEmissions.NitrogenExcretionRate = this.CalculateNitrogenExcretionRateChickens(
                    proteinIntake: dailyEmissions.ProteinIntake,
                    proteinRetained: dailyEmissions.ProteinRetained);
            }

            // Equation 4.2.1-29 (used in volatilization calculation)
            dailyEmissions.AmountOfNitrogenExcreted = base.CalculateAmountOfNitrogenExcreted(
                nitrogenExcretionRate: dailyEmissions.NitrogenExcretionRate,
                numberOfAnimals: managementPeriod.NumberOfAnimals);

            // Equation 4.2.1-30
            dailyEmissions.RateOfNitrogenAddedFromBeddingMaterial =
                base.CalculateRateOfNitrogenAddedFromBeddingMaterial(
                    beddingRate: managementPeriod.HousingDetails.UserDefinedBeddingRate,
                    nitrogenConcentrationOfBeddingMaterial: managementPeriod.HousingDetails
                        .TotalNitrogenKilogramsDryMatterForBedding,
                    beddingMaterialType: managementPeriod.HousingDetails.BeddingMaterialType);

            // Equation 4.2.1-31
            dailyEmissions.AmountOfNitrogenAddedFromBedding = base.CalculateAmountOfNitrogenAddedFromBeddingMaterial(
                rateOfNitrogenAddedFromBedding: dailyEmissions.RateOfNitrogenAddedFromBeddingMaterial,
                numberOfAnimals: managementPeriod.NumberOfAnimals);

            // Equation 4.2.2-1
            dailyEmissions.ManureDirectN2ONEmissionRate = base.CalculateManureDirectNitrogenEmissionRate(
                nitrogenExcretionRate: dailyEmissions.NitrogenExcretionRate,
                emissionFactor: managementPeriod.ManureDetails.N2ODirectEmissionFactor);

            // Equation 4.2.2-2
            dailyEmissions.ManureDirectN2ONEmission = base.CalculateManureDirectNitrogenEmission(
                manureDirectNitrogenEmissionRate: dailyEmissions.ManureDirectN2ONEmissionRate,
                numberOfAnimals: managementPeriod.NumberOfAnimals);

            /*
             * Indirect manure N2O
             */

            /*
             * Volatilization
             */

            // Equation 4.3.3-1
            dailyEmissions.AmmoniaConcentrationInHousing = this.CalculateAmmoniaLossFromHousing(
                yearlyTanExcreted: managementPeriod.ManureDetails.YearlyTanExcretion,
                housingEmissionFactor: managementPeriod.HousingDetails.AmmoniaEmissionFactorForHousing);

            // Equation 4.3.3-2
            dailyEmissions.AmmoniaEmissionsFromHousingSystem = this.CalculateAmmoniaEmissionsFromHousing(
                ammoniaLossFromHousing: dailyEmissions.AmmoniaConcentrationInHousing);

            // Equation 4.3.3-3
            dailyEmissions.AmmoniaLostFromStorage = this.CalculateAmmoniaLossFromStorage(
                yearlyTanExcreted: managementPeriod.ManureDetails.YearlyTanExcretion,
                emissionFactorForStorage: managementPeriod.ManureDetails.AmmoniaEmissionFactorForManureStorage);

            // Equation 4.3.3-4
            dailyEmissions.AmmoniaEmissionsFromStorageSystem = this.CalculateAmmoniaEmissionsFromStorage(
                ammoniaLossFromStorage: dailyEmissions.AmmoniaLostFromStorage);

            if (managementPeriod.ManureDetails.UseCustomVolatilizationFraction)
            {
                // Equation 4.3.3-1
                dailyEmissions.FractionOfManureVolatilized = managementPeriod.ManureDetails.VolatilizationFraction;
            }
            else
            {
                // Equation 4.3.3-1
                dailyEmissions.FractionOfManureVolatilized = this.CalculateFractionOfManureVolatilized(
                    ammoniaEmissionsFromHousing: dailyEmissions.AmmoniaConcentrationInHousing,
                    ammoniaEmissionsFromStorage: dailyEmissions.AmmoniaLostFromStorage,
                    amountOfNitrogenExcreted: dailyEmissions.AmountOfNitrogenExcreted,
                    amountOfNitrogenFromBedding: dailyEmissions.AmountOfNitrogenAddedFromBedding);
            }

            // Equation 4.3.3-3
            dailyEmissions.ManureVolatilizationRate = base.CalculateManureVolatilizationEmissionRateNonBeefCattle(
                nitrogenExcretionRate: dailyEmissions.NitrogenExcretionRate,
                volatilizationFraction: dailyEmissions.FractionOfManureVolatilized,
                volatilizationEmissionFactor: managementPeriod.ManureDetails.EmissionFactorVolatilization);

            // Equation 4.3.3-4
            dailyEmissions.ManureVolatilizationN2ONEmission = base.CalculateManureVolatilizationNitrogenEmission(
                volatilizationRate: dailyEmissions.ManureVolatilizationRate,
                numberOfAnimals: managementPeriod.NumberOfAnimals);

            /*
             * Leaching
             */

            // Equation 4.3.4-1
            dailyEmissions.ManureNitrogenLeachingRate = base.CalculateManureLeachingNitrogenEmissionRate(
                nitrogenExcretionRate: dailyEmissions.NitrogenExcretionRate,
                leachingFraction: managementPeriod.ManureDetails.LeachingFraction,
                emissionFactorForLeaching: managementPeriod.ManureDetails.EmissionFactorLeaching);

            // Equation 4.3.4-2
            dailyEmissions.ManureN2ONLeachingEmission = this.CalculateManureLeachingNitrogenEmission(
                leachingNitrogenEmissionRate: dailyEmissions.ManureNitrogenLeachingRate,
                numberOfAnimals: managementPeriod.NumberOfAnimals);

            // Equation 4.3.5-1
            dailyEmissions.ManureIndirectN2ONEmission = base.CalculateManureIndirectNitrogenEmission(
                manureVolatilizationNitrogenEmission: dailyEmissions.ManureVolatilizationN2ONEmission,
                manureLeachingNitrogenEmission: dailyEmissions.ManureN2ONLeachingEmission);

            // Equation 4.3.7-1
            dailyEmissions.ManureN2ONEmission = base.CalculateManureNitrogenEmission(
                manureDirectNitrogenEmission: dailyEmissions.ManureDirectN2ONEmission,
                manureIndirectNitrogenEmission: dailyEmissions.ManureIndirectN2ONEmission);

            // Equation 4.5.2-6
            dailyEmissions.NitrogenAvailableForLandApplication = base
                .CalculateTotalAvailableManureNitrogenInStoredManureForSheepSwinePoultryAndOtherLivestock(
                    nitrogenExcretion: dailyEmissions.AmountOfNitrogenExcreted,
                    nitrogenFromBedding: dailyEmissions.AmountOfNitrogenAddedFromBedding,
                    directN2ONEmission: dailyEmissions.ManureDirectN2ONEmission,
                    indirectN2ONEmission: dailyEmissions.ManureIndirectN2ONEmission);

            // Equation 4.5.3-1
            dailyEmissions.ManureCarbonNitrogenRatio = base.CalculateManureCarbonToNitrogenRatio(
                carbonFromStorage: dailyEmissions.AmountOfCarbonInStoredManure,
                nitrogenFromManure: dailyEmissions.NitrogenAvailableForLandApplication);

            // Equation 4.5.3-2
            dailyEmissions.TotalVolumeOfManureAvailableForLandApplication =
                base.CalculateTotalVolumeOfManureAvailableForLandApplication(
                    totalNitrogenAvailableForLandApplication: dailyEmissions.NitrogenAvailableForLandApplication,
                    nitrogenFractionOfManure: managementPeriod.ManureDetails.FractionOfNitrogenInManure);

            // Equation 4.6.2-2
            dailyEmissions.AmmoniaEmissionsFromLandAppliedManure =
                base.CalculateTotalAmmoniaEmissionsFromLandAppliedManure(
                    farm: farm,
                    dateTime: dateTime,
                    dailyEmissions: dailyEmissions,
                    animalType: animalGroup.GroupType,
                    temperature: temperature,
                    managementPeriod: managementPeriod);

            return dailyEmissions;
        }


        protected override void CalculateEnergyEmissions(GroupEmissionsByMonth groupEmissionsByMonth, Farm farm)
        {
            if (groupEmissionsByMonth.MonthsAndDaysData.ManagementPeriod.AnimalType.IsNewlyHatchedEggs() || groupEmissionsByMonth.MonthsAndDaysData.ManagementPeriod.AnimalType.IsEggs())
            {
                return;
            }

            var energyConversionFactor  = _energyConversionDefaultsProvider.GetElectricityConversionValue(groupEmissionsByMonth.MonthsAndDaysData.Year, farm.Province);
            groupEmissionsByMonth.MonthlyEnergyCarbonDioxide = this.CalculateTotalEnergyCarbonDioxideEmissionsFromPoultryOperations(
                numberOfAnimals: groupEmissionsByMonth.MonthsAndDaysData.ManagementPeriod.NumberOfAnimals,
                numberOfDays: groupEmissionsByMonth.MonthsAndDaysData.DaysInMonth,
                energyConversion: energyConversionFactor);
        }

        #endregion

        #region Equations

        /// <summary>
        /// Equation 4.3.3-1
        /// </summary>
        /// <param name="yearlyTanExcreted">TAN excreted by animals per year</param>
        /// <param name="housingEmissionFactor">Default fraction of TAN emission as NH3</param>
        /// <returns>Ammonia lost from housing (kg NH3-N day^-1)</returns>
        public double CalculateAmmoniaLossFromHousing(
            double yearlyTanExcreted,
            double housingEmissionFactor)
        {
            return (yearlyTanExcreted / 365) * housingEmissionFactor;
        }

        /// <summary>
        /// Equation 4.3.3-2
        /// </summary>
        /// <param name="ammoniaLossFromHousing">Ammonia lost from housing (kg NH3-N day^-1)</param>
        /// <returns>Total ammonia emissions from housing (kg NH3 day^-1)</returns>
        public double CalculateAmmoniaEmissionsFromHousing(
            double ammoniaLossFromHousing)
        {
            return ammoniaLossFromHousing * CoreConstants.ConvertNH3NToNH3;
        }

        /// <summary>
        /// Equation 4.3.3-3
        /// </summary>
        /// <param name="yearlyTanExcreted">TAN excreted by animals per year</param>
        /// <param name="emissionFactorForStorage">Default fraction of TAN emission as NH3</param>
        /// <returns>Ammonia lost from housing (kg NH3-N day^-1)</returns>
        public double CalculateAmmoniaLossFromStorage(
            double yearlyTanExcreted,
            double emissionFactorForStorage)
        {
            return (yearlyTanExcreted / 365) * emissionFactorForStorage;
        }

        /// <summary>
        /// Equation 4.3.3-4
        /// </summary>
        /// <param name="ammoniaLossFromStorage">Ammonia lost from storage (kg NH3-N day^-1)</param>
        /// <returns>Total ammonia emissions from storage (kg NH3 day^-1)</returns>
        public double CalculateAmmoniaEmissionsFromStorage(
            double ammoniaLossFromStorage)
        {
            return ammoniaLossFromStorage * CoreConstants.ConvertNH3NToNH3;
        }

        /// <summary>
        /// Equation 3.5.1-1
        /// </summary>
        /// <param name="entericMethaneEmissionRate">Enteric CH4 emission rate (kg head^-1 year^-1)</param>
        /// <param name="numberOfPoultry">Number of poultry</param>
        /// <param name="numberOfDaysInMonth">Number of days in month</param>
        /// <returns>Enteric CH4 emission (kg CH4)</returns>
        public double CalculateEntericMethaneEmission(double entericMethaneEmissionRate, 
                                                      double numberOfPoultry,
                                                      double numberOfDaysInMonth)
        {
            return entericMethaneEmissionRate * numberOfPoultry * numberOfDaysInMonth / CoreConstants.DaysInYear;
        }

        /// <summary>
        ///   Equation 3.5.2-1
        /// </summary>
        /// <param name="manureMethaneEmissionRate">Manure CH4 emission rate (kg head^-1 year^-1) </param>
        /// <param name="numberOfPoultry">Number of poultry</param>
        /// <param name="numberOfDaysInMonth">Number of days in month</param>
        /// <returns>Manure CH4 emission (kg CH4)</returns>
        public double CalculateManureMethaneEmission(double manureMethaneEmissionRate, 
                                                     double numberOfPoultry,
                                                     double numberOfDaysInMonth)
        {
            return manureMethaneEmissionRate * numberOfPoultry * numberOfDaysInMonth / CoreConstants.DaysInYear;
        }

        /// <summary>
        ///   Equation 3.5.3-1
        /// </summary>
        /// <param name="nitrogenExcretionRate">N excretion rate (kg head^-1 year^-1)</param>
        /// <param name="numberOfPoultry">Number of poultry</param>
        /// <param name="numberOfDays">Number of days in month</param>
        /// <returns>Manure N (kg N day-1)</returns>
        public double CalculateManureNitrogen(double nitrogenExcretionRate, 
                                              double numberOfPoultry, 
                                              double numberOfDays)
        {
            return numberOfDays * nitrogenExcretionRate * numberOfPoultry / CoreConstants.DaysInYear;
        }

        /// <summary>
        ///  Equation 3.5.3-2
        /// </summary>
        /// <param name="manureNitrogenExcretionRate">Manure direct N emission (kg N2O-N)</param>
        /// <param name="emissionFactor">Emission factor [kg N2O-N (kg N)^-1] </param>
        /// <param name="numberOfDays">Number of days in month</param>
        /// <returns>Manure direct N emission (kg N2O-N)</returns>
        public double CalculateManureDirectNitrogenEmission(double manureNitrogenExcretionRate, 
                                                                double emissionFactor,
                                                                double numberOfDays)
        {
            return manureNitrogenExcretionRate * emissionFactor * numberOfDays;
        }

        /// <summary>
        /// Equation 4.2.1-25
        /// </summary>
        /// <param name="dryMatterIntake">Dry matter intake (kg head^-1 day^-1)</param>
        /// <param name="crudeProtein">Crude protein content in dietary dry matter (% of DM)</param>
        /// <returns>Protein intake (kg head^-1 day^-1)</returns>
        private double CalculateProteinIntakePoultry(
            double dryMatterIntake, 
            double crudeProtein)
        {
            return dryMatterIntake * crudeProtein;
        }

        /// <summary>
        /// Equation 4.2.1-26
        /// </summary>
        /// <param name="proteinInLiveWeight">Average protein content in live weight (kg protein (kg head)^-1)</param>
        /// <param name="weightGain">Average daily weight gain for cohort (kg head^-1 day^-1)</param>
        /// <param name="proteinContentOfEggs">Average protein content of eggs (kg protein (kg egg)^-1)</param>
        /// <param name="eggProduction">Egg mass production (g egg head-1 day-1)</param>
        /// <returns>Protein retained in animal in cohort (kg head^-1 day^-1)</returns>
        private double CalculateProteinRetainedLayers(
            double proteinInLiveWeight,
            double weightGain,
            double proteinContentOfEggs,
            double eggProduction)
        {
            var result = (proteinInLiveWeight * weightGain) + ((proteinContentOfEggs * eggProduction) / 1000);

            return result;
        }

        /// <summary>
        /// Equation 4.2.1-27
        /// </summary>
        /// <param name="initialWeight">Live weight of the animal at the beginning of the stage (kg)</param>
        /// <param name="finalWeight">Live weight of the animal at the end of the stage (kg)</param>
        /// <param name="productionPeriod">Length of time from chick to slaughter</param>
        /// <returns>Protein retained in animal (kg head^-1 day^-1)</returns>
        private double CalculateProteinRetainedBroilers(
            double initialWeight,
            double finalWeight,
            double productionPeriod)
        {
            const double proteinRetainedForGain = 0.175;

            var result = ((finalWeight - initialWeight) * proteinRetainedForGain) / productionPeriod;

            return result;
        }

        /// <summary>
        /// Equation 4.2.1-28
        /// </summary>
        /// <param name="proteinIntake">Protein intake (kg head^-1 day^-1)</param>
        /// <param name="proteinRetained">Protein retained in animal in cohort (kg head^-1 day^-1)</param>
        /// <returns>N excretion rate (kg head-1 day-1)</returns>
        private double CalculateNitrogenExcretionRateChickens(
            double proteinIntake,
            double proteinRetained)
        {
            // Conversion from dietary protein to dietary N
            const double conversion = 6.25;

            var result = (proteinIntake / conversion) - (proteinRetained / conversion);

            return result;
        }


        /// <summary>
        ///  Equation 3.5.3-3
        /// </summary>
        /// <param name="manureNitrogenExcretionRate">Manure direct N emission (kg N2O-N)</param>
        /// <param name="volatilizationFraction">Volatilization fraction</param>
        /// <param name="emissionFactorForVolatilization">Emission factor for volatilization [kg N2O-N (kg N)^-1]</param>
        /// <param name="numberOfDaysInMonth">Number of days in month</param>
        /// <returns>Manure volatilization N emission (kg N2O-N)</returns>
        public double CalculateManureVolatilizationNitrogenEmission(double manureNitrogenExcretionRate,
                                                                    double volatilizationFraction, 
                                                                    double emissionFactorForVolatilization, 
                                                                    double numberOfDaysInMonth)
        {
            return manureNitrogenExcretionRate * volatilizationFraction * emissionFactorForVolatilization * numberOfDaysInMonth;
        }

        /// <summary>
        ///   Equation 3.5.3-4
        /// </summary>
        /// <param name="manureNitrogenExcretionRate">Manure N (kg N day^-1)</param>
        /// <param name="leachingFraction">Leaching fraction</param>
        /// <param name="emissionFactorForLeaching">Emission factor for leaching [kg N2O-N (kg N)^-1]</param>
        /// <param name="numberOfDaysInMonth">Number of days in month</param>
        /// <returns>Manure leaching N emission (kg N2O-N)</returns>
        public double CalculateManureLeachingNitrogenEmission(double manureNitrogenExcretionRate, 
                                                              double leachingFraction,
                                                              double emissionFactorForLeaching, 
                                                              double numberOfDaysInMonth)
        {
            return manureNitrogenExcretionRate * leachingFraction * emissionFactorForLeaching * numberOfDaysInMonth;
        }

        /// <summary>
        ///   Equation  3.5.3-6
        /// </summary>
        /// <param name="manureNitrogenExcretionRate">Manure N (kg N day^-1)</param>
        /// <param name="volatilizationFraction">Volatilization Fraction</param>
        /// <param name="leachingFraction">Leaching fraction</param>
        /// <param name="numberOfDays">Number of days in month</param>
        /// <returns>Manure available for land application (kg N)</returns>
        public double CalculateManureAvailableForLandApplication(double manureNitrogenExcretionRate, 
                                                                 double volatilizationFraction,
                                                                 double leachingFraction, 
                                                                 double numberOfDays)
        {
            var factor = 1 - (volatilizationFraction + leachingFraction);
            return manureNitrogenExcretionRate * factor * numberOfDays;
        }


        /// <summary>
        /// Equation 6.2.3-1
        /// </summary>
        /// <param name="numberOfAnimals">Barn capacity for poultry</param>
        /// <param name="numberOfDays">Number of days in month</param>
        /// <param name="energyConversion">kWh per poultry placement per year for electricity electricity (kWh poultry placement^-1 year^-1) </param>
        /// <returns>Total CO2 emissions from poultry operations (kg CO2)</returns>
        public double CalculateTotalEnergyCarbonDioxideEmissionsFromPoultryOperations(
            double numberOfAnimals, 
            double numberOfDays, 
            double energyConversion)
        {
            const double poultryConversion = 2.88;

            return numberOfAnimals * (poultryConversion / CoreConstants.DaysInYear) * energyConversion * numberOfDays;
        }

        #endregion
    }
}
