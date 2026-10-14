using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Agent.Core.Confirm;
using Agent.Core.Wallet;

namespace Agent.Core.Tests.Architecture;

/// <summary>
/// Rule 1, "no tap, no money", checked on the compiled code: the IL of every method in <c>Agent.Core</c> is decoded and
/// the only methods that call <see cref="WalletClient"/>'s execute are the two Confirm handler methods. Pure: no
/// database, no wallet.
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
        var callers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in AgentCore.GetTypes())
        {
            foreach (var method in MethodsOf(type))
            {
                if (CallsExecute(method))
                    callers.Add(NameOf(UserFacing(method)));
            }
        }

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

    /// <summary>True when the IL of <paramref name="method"/> calls, loads or constructs <c>WalletClient.ExecuteAsync</c>.</summary>
    private static bool CallsExecute(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
            return false;

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

            if (MethodOperandOpCodes.Contains(code.Value))
            {
                var token = BitConverter.ToInt32(il, position);
                if (Resolve(method, token) is { } callee && IsExecute(callee))
                    return true;
            }

            position += OperandSize(code, il, position);
        }

        return false;
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
