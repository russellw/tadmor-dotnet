using Tadmor;
using Tadmor.Commands;

// Out-of-band commands come first; anything else runs the server.
return args switch
{
    ["adduser", .. var rest] => await AddUser.RunAsync(rest),
    ["resetdb", .. var rest] => await ResetDb.RunAsync(rest),
    _ => await Server.RunAsync(args),
};
