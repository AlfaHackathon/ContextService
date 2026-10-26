using ContextService.Contracts.Knowledge;

namespace ContextService.Services;

// Search features retain raw owner fields; geographic mismatch never creates a hard exclusion.
public static class CompanyBankProfileSearchText
{
    public static string Build(CompanyBankProfile profile)
    {
        var bank = profile.BankData;
        var features = new List<string>();
        void Add(string key, string? value)
        {
            if (value is not null)
            {
                features.Add(key + ": " + value);
            }
        }
        Add("REGION", bank.Region);
        Add("CITY", bank.City);
        Add("MAIN_OKVED_CODE", bank.MainOkvedCode);
        Add("MAIN_OKVED_NAME", bank.MainOkvedName);
        Add("ORG_TYPE", bank.OrgType);
        Add("OKFS_NAME", bank.OkfsName);
        Add("COMPANY_AGE_YEARS_GRADATION", bank.CompanyAgeYearsGradation);
        Add("ECO_MARK_LABEL", bank.EcoMarkLabel);
        Add("NEGATIVE_MARK", bank.NegativeMark);
        Add("AMOUNT_WORKERS_FNS_GRADATION", bank.AmountWorkersFnsGradation);
        Add("AMOUNT_WORKERS_SPARK_GRADATION", bank.AmountWorkersSparkGradation);
        Add("CL_TYPE", bank.ClType);
        Add("CL_PRODUCT_RKO_MARK", bank.ClProductRkoMark);
        Add("CL_PRODUCT_GAR_MARK", bank.ClProductGarMark);
        Add("CL_PRODUCT_LOA_MARK", bank.ClProductLoaMark);
        Add("CL_PRODUCT_LEA_MARK", bank.ClProductLeaMark);
        Add("CL_PRODUCT_DEP_MARK", bank.ClProductDepMark);
        Add("CL_PRODUCT_ZPP_MARK", bank.ClProductZppMark);
        Add("FTS_FLAG", bank.FtsFlag);
        Add("FTS_COUNTRY_NAME", bank.FtsCountryName);
        Add("FACTORING_MARK", bank.FactoringMark);
        Add("REVENUE_COMPANY", bank.RevenueCompany);
        Add("GROUP_ID", bank.GroupId?.ToString("D"));
        return string.Join('\n', features);
    }
}
