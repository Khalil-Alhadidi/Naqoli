using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Agent.Core.Confirm;
using Agent.Core.Wallet;

namespace Agent.Core.Tests.Architecture;

/// <summary>
/// Rule 1, "no tap, no money", checked on the compiled code: the IL of every method in <c>Agent.Core</c> is decoded and
/// the only methods that call <see cref="WalletClient"/>'s execute are the two Confirm handler methods; nothing in
/// <c>Agent.Core</c> calls those two; only the wallet client names the <c>transactions</c> routes; and nothing invokes a
/// method by reflection. Pure: no database, no wallet.
/// </summary>
public class NoTapNoMoneyTests
{
    private const string ExecuteName = "ExecuteAsync";

    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Assembly AgentCore = typeof(WalletClient).Assembly;

    /// <summary>The opcodes whose operand is a method token.</summary>
    private static readonly HashSet<short> MethodOperandOpCodes =
    [
        OpCodes.Call.Value, OpCodes.Callvirt.Value, OpCodes.Ldftn.Value, OpCodes.Ldvirtftn.Value, OpCodes.Newobj.Value,
    ];

    private static readonly Dictionary<short, OpCode> OneByteOpCodes = new();
    private static readonly Dictionary<short, OpCode> TwoByteOpCodes = new();

    static NoTapNoMoneyTests()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode code)
                continue;
            if (code.Size == 1)
                OneByteOpCodes[code.Value] = code;
            else
                TwoByteOpCodes[unchecked((short)(code.Value & 0xFF))] = code;
        }
    }

    [Fact]
    public void OnlyTheConfirmHandlerCanReachExecute()
    {
        var callers = CallersOf(IsExecute);

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{typeof(ConfirmHandler).FullName}.{nameof(ConfirmHandler.ConfirmAsync)}",
            $"{typeof(ConfirmHandler).FullName}.{nameof(ConfirmHandler.SettleCheckingAsync)}",
        };
        Assert.True(
            callers.SetEquals(expected),
            "The methods that reach WalletClient.ExecuteAsync must be exactly the Confirm handler's two. Found: "
            + string.Join(", ", callers.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Nothing in <c>Agent.Core</c> calls <see cref="ConfirmHandler.ConfirmAsync"/> or
    /// <see cref="ConfirmHandler.SettleCheckingAsync"/>: no parse or reply path can turn words into a Confirm. Milestone 4
    /// adds exactly the card component's Confirm handler and the settle timer in <c>Agent.Web</c> as callers, and must
    /// extend this scan to that assembly with them as the only allowed callers.
    /// </summary>
    [Fact]
    public void NothingInAgentCoreCallsTheConfirmHandler()
    {
        var callers = CallersOf(callee =>
            callee.DeclaringType == typeof(ConfirmHandler)
            && callee.Name is nameof(ConfirmHandler.ConfirmAsync) or nameof(ConfirmHandler.SettleCheckingAsync));

        Assert.True(
            callers.Count == 0,
            "Nothing in Agent.Core may call the Confirm handler. Found: " + string.Join(", ", callers.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// The wallet's money-moving route is named in one place: the literal <c>"transactions"</c> is loaded only by
    /// <see cref="WalletClient"/>'s execute, and <c>"transactions/by-key/"</c> only by its read by key. Any other
    /// string literal starting with <c>transactions</c> fails the test, so no second HTTP path to the route can hide.
    /// </summary>
    [Fact]
    public void OnlyTheWalletClientNamesTheTransactionsRoutes()
    {
        var users = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var type in AgentCore.GetTypes())
        {
            foreach (var method in MethodsOf(type))
            {
                foreach (var literal in StringsLoadedBy(method).Where(s => s.StartsWith("transactions", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!users.TryGetValue(literal, out var names))
                        users[literal] = names = new SortedSet<string>(StringComparer.Ordinal);
                    names.Add(NameOf(UserFacing(method)));
                }
            }
        }

        var client = typeof(WalletClient).FullName;
        Assert.Equal(["transactions", "transactions/by-key/"], users.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([$"{client}.{ExecuteName}"], users["transactions"]);
        Assert.Equal([$"{client}.{nameof(WalletClient.FindByKeyAsync)}"], users["transactions/by-key/"]);
    }

    /// <summary>No method in <c>Agent.Core</c> invokes a method by reflection, so none can reach execute that way.</summary>
    [Fact]
    public void NothingInAgentCoreInvokesByReflection()
    {
        var callers = CallersOf(callee =>
            (callee.DeclaringType is { } declaring && typeof(MethodBase).IsAssignableFrom(declaring) && callee.Name == nameof(MethodBase.Invoke))
            || (callee.DeclaringType is { } owner && typeof(Type).IsAssignableFrom(owner)
                && callee.Name is nameof(Type.GetMethod) or nameof(Type.GetMethods)));

        Assert.True(
            callers.Count == 0,
            "Nothing in Agent.Core may invoke a method by reflection. Found: " + string.Join(", ", callers.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void ExecuteIsInternal()
    {
        var execute = typeof(WalletClient).GetMethod(ExecuteName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(execute);
        Assert.False(execute.IsPublic, "WalletClient.ExecuteAsync must not be public.");
        Assert.True(execute.IsAssembly, "WalletClient.ExecuteAsync must be internal.");

        var friends = AgentCore.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToList();
        Assert.Equal(["Agent.Core.Tests"], friends);
    }

    private static IEnumerable<MethodBase> MethodsOf(Type type)
        => type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything));

    /// <summary>The user-facing names of every method in <c>Agent.Core</c> whose IL references a method matching <paramref name="target"/>.</summary>
    private static HashSet<string> CallersOf(Func<MethodBase, bool> target)
    {
        var callers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in AgentCore.GetTypes())
        {
            foreach (var method in MethodsOf(type))
            {
                if (Calls(method, target))
                    callers.Add(NameOf(UserFacing(method)));
            }
        }

        return callers;
    }

    /// <summary>True when the IL of <paramref name="method"/> calls, loads or constructs a method matching <paramref name="target"/>.</summary>
    private static bool Calls(MethodBase method, Func<MethodBase, bool> target)
    {
        foreach (var (code, token) in Instructions(method))
        {
            if (MethodOperandOpCodes.Contains(code.Value) && Resolve(method, token) is { } callee && target(callee))
                return true;
        }

        return false;
    }

    /// <summary>The string literals (<c>ldstr</c>) in the IL of <paramref name="method"/>.</summary>
    private static IEnumerable<string> StringsLoadedBy(MethodBase method)
    {
        foreach (var (code, token) in Instructions(method))
        {
            if (code.Value == OpCodes.Ldstr.Value)
                yield return method.Module.ResolveString(token);
        }
    }

    /// <summary>
    /// Every instruction of <paramref name="method"/>'s IL with its operand read as a 32-bit token (meaningful only for
    /// the opcodes that take a token).
    /// </summary>
    private static List<(OpCode Code, int Token)> Instructions(MethodBase method)
    {
        var instructions = new List<(OpCode, int)>();
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
            return instructions;

        var position = 0;
        while (position < il.Length)
        {
            OpCode code;
            if (il[position] == 0xFE)
            {
                code = TwoByteOpCodes[il[position + 1]];
                position += 2;
            }
            else
            {
                code = OneByteOpCodes[il[position]];
                position += 1;
            }

            var size = OperandSize(code, il, position);
            instructions.Add((code, size == 4 ? BitConverter.ToInt32(il, position) : 0));
            position += size;
        }

        return instructions;
    }

    private static MethodBase? Resolve(MethodBase method, int token)
    {
        try
        {
            var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            return method.Module.ResolveMethod(token, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static bool IsExecute(MethodBase callee)
        => callee.DeclaringType == typeof(WalletClient) && callee.Name == ExecuteName;

    private static int OperandSize(OpCode code, byte[] il, int position) => code.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
        _ => 4,
    };

    /// <summary>
    /// The method a person wrote: a compiler-generated state machine's <c>MoveNext</c> (or a lambda's or local
    /// function's body inside a generated class) maps back to the method that declares it.
    /// </summary>
    private static MethodBase UserFacing(MethodBase method)
    {
        var type = method.DeclaringType;
        while (type is not null && type.IsDefined(typeof(CompilerGeneratedAttribute)) && type.DeclaringType is { } outer)
        {
            var owner = outer.GetMethods(Everything).FirstOrDefault(m =>
                m.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType == type
                || m.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType == type);
            if (owner is null)
            {
                // A state machine is named <Method>d__N.
                var name = type.Name;
                if (name.StartsWith('<') && name.IndexOf('>', StringComparison.Ordinal) is > 1 and var end)
                    owner = outer.GetMethods(Everything).FirstOrDefault(m => m.Name == name[1..end]);
            }

            if (owner is not null)
                return owner;

            type = outer;
        }

        return method;
    }

    private static string NameOf(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";
}
