using System;
using System.Collections.Generic;

namespace TodoApi;
public class RegisterDto
{
    public string UserName { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string Email { get; set; } = null!;

}
