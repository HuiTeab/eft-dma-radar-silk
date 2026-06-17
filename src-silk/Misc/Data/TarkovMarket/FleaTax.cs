// Copyright (c) 2025 HuiTeab.
// Licensed under the PolyForm Noncommercial License 1.0.0.
// See LICENSE in the repository root for details.

namespace eft_dma_radar.Silk.Misc.Data.TarkovMarket
{
    internal static class FleaTax
    {
        private const double CommunityItemTax = 3d;
        private const double CommunityRequirementTax = 3d;
        private const double RagFairCommissionModifier = 1d;
        // The markup side of the listing is curved by this exponent before being raised over the base.
        private const double PriceFactorExponent = 1.08d;
        private const double PriceFactorBase = 4.0d;

        /// <summary>
        /// Calculates the flea market tax for a listing.
        /// </summary>
        /// <param name="requirementsPrice">Desired flea list price.</param>
        /// <param name="basePrice">Item base price from the API.</param>
        public static double Calculate(double requirementsPrice, double basePrice)
        {
            if (basePrice == 0d || requirementsPrice == 0d)
                return 0d;

            // Per-side tax rates, as fractions of the respective price.
            double itemTaxRate = CommunityItemTax / 100d;
            double requirementTaxRate = CommunityRequirementTax / 100d;

            // Log-scale ratios of base value against the asking price (one for each side).
            double offerFactor = Math.Log10(basePrice / requirementsPrice);
            double requestFactor = Math.Log10(requirementsPrice / basePrice);

            // Whichever side is the markup gets the steeper exponent, so overpricing is taxed harder.
            if (requirementsPrice >= basePrice)
                requestFactor = Math.Pow(requestFactor, PriceFactorExponent);
            else
                offerFactor = Math.Pow(offerFactor, PriceFactorExponent);

            offerFactor = Math.Pow(PriceFactorBase, offerFactor);
            requestFactor = Math.Pow(PriceFactorBase, requestFactor);

            double tax = basePrice * itemTaxRate * offerFactor + requirementsPrice * requirementTaxRate * requestFactor;
            return tax * RagFairCommissionModifier;
        }
    }
}
