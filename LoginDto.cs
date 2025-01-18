using System;
using System.Collections.Generic;

namespace TodoApi;
public class LoginDto
{
    public string UserName { get; set; } = null!;
    public string Password { get; set; } = null!;
}
