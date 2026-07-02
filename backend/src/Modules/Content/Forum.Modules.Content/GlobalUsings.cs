// The Thread aggregate would otherwise be ambiguous with System.Threading.Thread (implicit usings);
// a global using-alias wins name resolution over namespace imports, so unqualified `Thread` is ours.
global using Thread = Forum.Modules.Content.Domain.Threads.Thread;
