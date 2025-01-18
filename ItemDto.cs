using System;
using System.Collections.Generic;

namespace TodoApi;

public partial class ItemDto
{
    public string Name { get; set; } = null!;

    public bool? IsComplete { get; set; }
}
