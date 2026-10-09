using System.ComponentModel.DataAnnotations;

namespace AffiVideo.Contracts;

public sealed record SignInRequest([Required] string Email, [Required] string Password);

/// <summary>Who is signed in, and the Organization they work in.</summary>
public sealed record SessionResponse(MemberResponse Member, OrganizationResponse Organization);

/// <summary>
/// The value to send back in the X-CSRF-TOKEN header of a state-changing request.
/// It stops being valid when the caller signs in or out.
/// </summary>
public sealed record AntiforgeryTokenResponse(string RequestToken);
