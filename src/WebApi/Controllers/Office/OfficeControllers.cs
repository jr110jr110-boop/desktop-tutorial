using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.OfficeDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // Office automation (release O1). Every signed-in user can draft letters and requests and read their own inbox; the rule for each step
    // (positions, chain, roles SECRETARIAT and HR) is checked in the services.

    [ApiController]
    [Authorize]
    [Route("api/v1/office")]
    public class OfficeOrgController : ControllerBase
    {
        private readonly IOrgService _org;
        public OfficeOrgController(IOrgService org) { _org = org; }

        [HttpGet("units")]
        public async Task<IActionResult> UnitsAsync([FromQuery] OfficePageQuery query, CancellationToken ct) => Ok((await _org.GetUnitsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("units")]
        public async Task<IActionResult> SaveUnitAsync([FromBody] SaveUnitDto dto, CancellationToken ct) => Ok(await _org.SaveUnitAsync(dto, ct));

        [HttpGet("positions")]
        public async Task<IActionResult> PositionsAsync([FromQuery] PositionQuery query, CancellationToken ct) => Ok((await _org.GetPositionsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("positions")]
        public async Task<IActionResult> SavePositionAsync([FromBody] SavePositionDto dto, CancellationToken ct) => Ok(await _org.SavePositionAsync(dto, ct));

        [HttpGet("delegations")]
        public async Task<IActionResult> DelegationsAsync([FromQuery] DelegationQuery query, CancellationToken ct) => Ok((await _org.GetDelegationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("delegations")]
        public async Task<IActionResult> SaveDelegationAsync([FromBody] SaveDelegationDto dto, CancellationToken ct) => Ok(await _org.SaveDelegationAsync(dto, ct));

        [HttpPost("delegations/{id:int}/cancel")]
        public async Task<IActionResult> CancelDelegationAsync(int id, CancellationToken ct) => Ok(await _org.CancelDelegationAsync(id, ct));

        /// <summary>The holder of the position (or the CEO) approves a delegation made by somebody else. Not the one who made it.</summary>
        [HttpPost("delegations/{id:int}/approve")]
        public async Task<IActionResult> ApproveDelegationAsync(int id, CancellationToken ct) => Ok(await _org.DecideDelegationAsync(id, true, ct));

        [HttpPost("delegations/{id:int}/reject")]
        public async Task<IActionResult> RejectDelegationAsync(int id, CancellationToken ct) => Ok(await _org.DecideDelegationAsync(id, false, ct));

        [HttpGet("org/tree")]
        public async Task<IActionResult> TreeAsync(CancellationToken ct) => Ok(await _org.GetTreeAsync(ct));

        /// <summary>The signed-in user's positions, deputies and signature.</summary>
        [HttpGet("me")]
        public async Task<IActionResult> MineAsync(CancellationToken ct) => Ok(await _org.GetMineAsync(ct));

        [HttpPost("me/signature")]
        public async Task<IActionResult> SaveSignatureAsync([FromBody] SaveSignatureDto dto, CancellationToken ct) => Ok(await _org.SaveSignatureAsync(dto, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/office/inbox")]
    public class OfficeInboxController : ControllerBase
    {
        private readonly IInboxService _inbox;
        public OfficeInboxController(IInboxService inbox) { _inbox = inbox; }

        [HttpGet]
        public async Task<IActionResult> InboxAsync([FromQuery] InboxQuery query, CancellationToken ct) => Ok((await _inbox.GetInboxAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("counts")]
        public async Task<IActionResult> CountsAsync(CancellationToken ct) => Ok(await _inbox.GetCountsAsync(ct));

        /// <summary>Mark read or unread, or done (not for approvals, referrals and meeting actions, which close by their own action).</summary>
        [HttpPost("act")]
        public async Task<IActionResult> ActAsync([FromBody] InboxActionDto dto, CancellationToken ct) => Ok(await _inbox.ActAsync(dto, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/office")]
    public class OfficeLettersController : ControllerBase
    {
        private readonly ILetterService _letters;
        private readonly global::Services.Contracts.IDocumentService _documents;
        public OfficeLettersController(ILetterService letters, global::Services.Contracts.IDocumentService documents) { _letters = letters; _documents = documents; }

        /// <summary>An attachment, the signature image or the delivery receipt of a letter. Only somebody who may read the letter in full gets the file; everybody else gets 404.</summary>
        [HttpGet("letters/{id:int}/attachments/{documentId:guid}")]
        public async Task<IActionResult> LetterFileAsync(int id, System.Guid documentId, CancellationToken ct)
        {
            if (!await _letters.CanReadFileAsync(id, documentId, ct)) return NotFound();
            ViewModels.NasbNiro.DocumentDtos.ResponseDocumentDto file = await _documents.DownloadDocumentAsync(documentId);
            if (file == null) return NotFound();
            return File(file.FileStream, "application/x-download", file.Name);
        }

        [HttpGet("letters")]
        public async Task<IActionResult> LettersAsync([FromQuery] LetterQuery query, CancellationToken ct) => Ok((await _letters.GetLettersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("letters/{id:int}")]
        public async Task<IActionResult> LetterAsync(int id, CancellationToken ct) => Ok(await _letters.GetLetterAsync(id, ct));

        [HttpPost("letters")]
        public async Task<IActionResult> SaveLetterAsync([FromBody] SaveLetterDto dto, CancellationToken ct) => Ok(await _letters.SaveLetterAsync(dto, ct));

        /// <summary>submit, approve, return, sign, register, send, receipt, archive, cancel.</summary>
        [HttpPost("letters/action")]
        public async Task<IActionResult> ActionAsync([FromBody] LetterActionDto dto, CancellationToken ct) => Ok(await _letters.LetterActionAsync(dto, ct));

        /// <summary>Checks that the signed content still matches its signature hash.</summary>
        [HttpGet("letters/{id:int}/verify")]
        public async Task<IActionResult> VerifyAsync(int id, CancellationToken ct) => Ok(await _letters.VerifyAsync(id, ct));

        [HttpPost("letters/refer")]
        public async Task<IActionResult> ReferAsync([FromBody] ReferLetterDto dto, CancellationToken ct) => Ok(await _letters.ReferAsync(dto, ct));

        [HttpPost("letters/referral-action")]
        public async Task<IActionResult> ReferralActionAsync([FromBody] ReferralActionDto dto, CancellationToken ct) => Ok(await _letters.ReferralActionAsync(dto, ct));

        [HttpGet("parties")]
        public async Task<IActionResult> PartiesAsync([FromQuery] OfficePageQuery query, CancellationToken ct) => Ok((await _letters.GetPartiesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("parties")]
        public async Task<IActionResult> SavePartyAsync([FromBody] SaveExternalPartyDto dto, CancellationToken ct) => Ok(await _letters.SavePartyAsync(dto, ct));

        [HttpGet("numbering-rules")]
        public async Task<IActionResult> RulesAsync([FromQuery] OfficePageQuery query, CancellationToken ct) => Ok((await _letters.GetRulesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("numbering-rules")]
        public async Task<IActionResult> SaveRuleAsync([FromBody] SaveNumberingRuleDto dto, CancellationToken ct) => Ok(await _letters.SaveRuleAsync(dto, ct));

        [HttpGet("letter-templates")]
        public async Task<IActionResult> TemplatesAsync([FromQuery] OfficePageQuery query, CancellationToken ct) => Ok((await _letters.GetTemplatesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("letter-templates")]
        public async Task<IActionResult> SaveTemplateAsync([FromBody] SaveLetterTemplateDto dto, CancellationToken ct) => Ok(await _letters.SaveTemplateAsync(dto, ct));

        /// <summary>The old archive (for example the Rahkaran export): numbers are kept and the sequence is not consumed.</summary>
        [HttpPost("letters/import")]
        public async Task<IActionResult> ImportAsync([FromBody] ImportLettersDto dto, CancellationToken ct) => Ok(await _letters.ImportAsync(dto, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/office")]
    public class OfficeMeetingsController : ControllerBase
    {
        private readonly IMeetingService _meetings;
        public OfficeMeetingsController(IMeetingService meetings) { _meetings = meetings; }

        [HttpGet("meetings")]
        public async Task<IActionResult> MeetingsAsync([FromQuery] MeetingQuery query, CancellationToken ct) => Ok((await _meetings.GetMeetingsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("meetings/{id:int}")]
        public async Task<IActionResult> MeetingAsync(int id, CancellationToken ct) => Ok(await _meetings.GetMeetingAsync(id, ct));

        [HttpPost("meetings")]
        public async Task<IActionResult> SaveMeetingAsync([FromBody] SaveMeetingDto dto, CancellationToken ct) => Ok(await _meetings.SaveMeetingAsync(dto, ct));

        /// <summary>hold, submit-minutes, approve-minutes, sign-minutes, cancel.</summary>
        [HttpPost("meetings/action")]
        public async Task<IActionResult> ActionAsync([FromBody] MeetingActionDto dto, CancellationToken ct) => Ok(await _meetings.MeetingActionAsync(dto, ct));

        [HttpGet("decisions")]
        public async Task<IActionResult> DecisionsAsync([FromQuery] DecisionQuery query, CancellationToken ct) => Ok((await _meetings.GetDecisionsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("decisions/action")]
        public async Task<IActionResult> DecisionActionAsync([FromBody] DecisionActionDto dto, CancellationToken ct) => Ok(await _meetings.DecisionActionAsync(dto, ct));

        /// <summary>Alerts the chairs of overdue decisions now (the daily job does the same).</summary>
        [HttpPost("decisions/check-overdue")]
        [Authorize(Roles = "SECRETARIAT,PMO,CEO,HR")]
        public async Task<IActionResult> CheckOverdueAsync(CancellationToken ct) => Ok(await _meetings.CheckOverdueAsync(ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/office/attendance")]
    public class OfficeAttendanceController : ControllerBase
    {
        private readonly IAttendanceService _attendance;
        public OfficeAttendanceController(IAttendanceService attendance) { _attendance = attendance; }

        [HttpGet("shifts")]
        public async Task<IActionResult> ShiftsAsync(CancellationToken ct) => Ok(await _attendance.GetShiftsAsync(ct));

        [HttpPost("shifts")]
        public async Task<IActionResult> SaveShiftAsync([FromBody] SaveShiftDto dto, CancellationToken ct) => Ok(await _attendance.SaveShiftAsync(dto, ct));

        [HttpGet("shift-assignments")]
        public async Task<IActionResult> AssignmentsAsync([FromQuery] ShiftAssignmentQuery query, CancellationToken ct) => Ok((await _attendance.GetAssignmentsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("shift-assignments")]
        public async Task<IActionResult> SaveAssignmentAsync([FromBody] SaveShiftAssignmentDto dto, CancellationToken ct) => Ok(await _attendance.SaveAssignmentAsync(dto, ct));

        [HttpGet("devices")]
        public async Task<IActionResult> DevicesAsync(CancellationToken ct) => Ok(await _attendance.GetDevicesAsync(ct));

        /// <summary>The new key is returned once (DeviceKeyDto.ApiKey); only its hash is stored.</summary>
        [HttpPost("devices")]
        public async Task<IActionResult> SaveDeviceAsync([FromBody] SaveDeviceDto dto, CancellationToken ct) => Ok(await _attendance.SaveDeviceAsync(dto, ct));

        [HttpGet("clock")]
        public async Task<IActionResult> ClockAsync([FromQuery] ClockQuery query, CancellationToken ct) => Ok((await _attendance.GetClockEventsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("clock/manual")]
        public async Task<IActionResult> ManualAsync([FromBody] ManualClockDto dto, CancellationToken ct) => Ok(await _attendance.AddManualEventAsync(dto, ct));

        [HttpPost("clock/import")]
        public async Task<IActionResult> ImportAsync([FromBody] ImportClockDto dto, CancellationToken ct) => Ok(await _attendance.ImportEventsAsync(dto, ct));

        /// <summary>For devices and middleware on the LAN. Headers X-Device-Code and X-Device-Key; a wrong code or key gives 401.</summary>
        [HttpPost("push")]
        [AllowAnonymous]
        public async Task<IActionResult> PushAsync([FromBody] DevicePushDto dto, CancellationToken ct)
        {
            string deviceCode = Request.Headers["X-Device-Code"].ToString();
            string deviceKey = Request.Headers["X-Device-Key"].ToString();
            ResponseDto<DevicePushResultDto> result = await _attendance.PushAsync(deviceCode, deviceKey, dto, ct);
            if (result.HttpStatusCode == HttpStatusCode.Unauthorized) return StatusCode(401, result);
            return Ok(result);
        }

        [HttpGet("policy")]
        public async Task<IActionResult> PolicyAsync(CancellationToken ct) => Ok(await _attendance.GetPolicyAsync(ct));

        [HttpPost("policy")]
        public async Task<IActionResult> SavePolicyAsync([FromBody] AttendancePolicyDto dto, CancellationToken ct) => Ok(await _attendance.SavePolicyAsync(dto, ct));

        [HttpPost("compute")]
        public async Task<IActionResult> ComputeAsync([FromBody] ComputeDaysDto dto, CancellationToken ct) => Ok(await _attendance.ComputeDaysAsync(dto, ct));

        [HttpGet("days")]
        public async Task<IActionResult> DaysAsync([FromQuery] DayQuery query, CancellationToken ct) => Ok((await _attendance.GetDaysAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("close-month")]
        public async Task<IActionResult> CloseMonthAsync([FromBody] CloseMonthDto dto, CancellationToken ct) => Ok(await _attendance.CloseMonthAsync(dto, ct));

        [HttpGet("month/{year:int}/{month:int}")]
        public async Task<IActionResult> MonthAsync(int year, int month, CancellationToken ct) => Ok(await _attendance.GetMonthStatusAsync(year, month, ct));

        /// <summary>Hours present per person and day, for the production time-booking check.</summary>
        [HttpPost("worked-hours")]
        public async Task<IActionResult> WorkedHoursAsync([FromBody] WorkedHoursQuery query, CancellationToken ct) => Ok(await _attendance.GetWorkedHoursAsync(query, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/office")]
    public class OfficeLeavesController : ControllerBase
    {
        private readonly ILeaveService _leaves;
        public OfficeLeavesController(ILeaveService leaves) { _leaves = leaves; }

        [HttpGet("leaves")]
        public async Task<IActionResult> LeavesAsync([FromQuery] LeaveQuery query, CancellationToken ct) => Ok((await _leaves.GetLeavesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("leaves/{id:int}")]
        public async Task<IActionResult> LeaveAsync(int id, CancellationToken ct) => Ok(await _leaves.GetLeaveAsync(id, ct));

        [HttpPost("leaves")]
        public async Task<IActionResult> SaveLeaveAsync([FromBody] SaveLeaveDto dto, CancellationToken ct) => Ok(await _leaves.SaveLeaveAsync(dto, ct));

        /// <summary>submit, approve, record, reject, cancel.</summary>
        [HttpPost("leaves/action")]
        public async Task<IActionResult> ActionAsync([FromBody] LeaveActionDto dto, CancellationToken ct) => Ok(await _leaves.LeaveActionAsync(dto, ct));

        [HttpGet("leaves/balance")]
        public async Task<IActionResult> BalanceAsync([FromQuery] int? employeeId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct) => Ok(await _leaves.GetBalanceAsync(employeeId, year, month, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/office")]
    public class OfficeRequestsController : ControllerBase
    {
        private readonly IRequestService _requests;
        public OfficeRequestsController(IRequestService requests) { _requests = requests; }

        [HttpGet("request-types")]
        public async Task<IActionResult> TypesAsync(CancellationToken ct) => Ok(await _requests.GetTypesAsync(ct));

        [HttpPost("request-types")]
        public async Task<IActionResult> SaveTypeAsync([FromBody] SaveRequestTypeDto dto, CancellationToken ct) => Ok(await _requests.SaveTypeAsync(dto, ct));

        [HttpGet("requests")]
        public async Task<IActionResult> RequestsAsync([FromQuery] RequestQuery query, CancellationToken ct) => Ok((await _requests.GetRequestsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("requests/{id:int}")]
        public async Task<IActionResult> RequestAsync(int id, CancellationToken ct) => Ok(await _requests.GetRequestAsync(id, ct));

        [HttpPost("requests")]
        public async Task<IActionResult> SaveRequestAsync([FromBody] SaveRequestDto dto, CancellationToken ct) => Ok(await _requests.SaveRequestAsync(dto, ct));

        /// <summary>submit, approve, reject, cancel, complete.</summary>
        [HttpPost("requests/action")]
        public async Task<IActionResult> ActionAsync([FromBody] RequestActionDto dto, CancellationToken ct) => Ok(await _requests.RequestActionAsync(dto, ct));
    }
}
