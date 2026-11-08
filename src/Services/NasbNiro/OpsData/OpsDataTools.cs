using System.Collections.Generic;
using System.Threading.Tasks;
using Common.Results;
using Services.NasbNiro.Ai;
using ViewModels.NasbNiro.OpsDataDtos;

namespace Services.NasbNiro.OpsData
{
    /// <summary>
    /// Read-only tools for the CEO brief and the assistant: the open data-quality findings and the engineering readiness of the running projects. They change nothing; the numbers they return
    /// are the ones the numeric check of the answer (AnswerVerifier) compares the text against.
    /// </summary>
    public static class AiOpsDataTools
    {
        public const string QualityTool = "ops_data_quality";
        public const string ReadinessTool = "ops_readiness";

        public static List<AiToolDefinition> Build() => new List<AiToolDefinition>
        {
            new AiToolDefinition
            {
                Name = QualityTool, Title = "کیفیت داده عملیاتی", RequiredRoles = OpsRoles.Readers, CitationType = "Dashboard", PageLink = "/ops-data/quality",
                Description = "شمار یافته‌های باز کنترل داده (هشدار و مسدودی) به تفکیک واحد و قانون، استثناهای این هفته و استثناهای منتظر تصمیم، و پرتکرارترین قانون‌ها.",
                Run = async (c, a, ct) =>
                {
                    ResponseDto<OpsSummaryDto> r = await c.Get<IOpsDataQualityService>().SummaryAsync(null, ct);
                    if (!r.Success) throw new AiToolFailureException(r.Message);
                    return r.Data;
                },
            },
            new AiToolDefinition
            {
                Name = ReadinessTool, Title = "آمادگی اطلاعات مهندسی پروژه‌ها", RequiredRoles = OpsReadinessService.Readers, CitationType = "Dashboard", PageLink = "/ops-data/quality?tab=readiness",
                Description = "درصد آمادگی اطلاعات مهندسی (BOM، WBS، OPC، نقشه، ITP و ...) هر پروژه‌ی در حال اجرا، تعداد موارد سخت و نرم ناقص و تعهدهای باز و دیرکرد.",
                Run = async (c, a, ct) =>
                {
                    ResponseDto<ReadinessSummaryDto> r = await c.Get<IOpsReadinessService>().SummaryAsync(ct);
                    if (!r.Success) throw new AiToolFailureException(r.Message);
                    return r.Data;
                },
            },
        };
    }
}
