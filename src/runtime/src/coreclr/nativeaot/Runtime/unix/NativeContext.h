// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef __NATIVE_CONTEXT_H__
#define __NATIVE_CONTEXT_H__

#if HAVE_UCONTEXT_H
#include <ucontext.h>
#else
#include <signal.h>
#endif

// Convert Unix native context to PAL_LIMITED_CONTEXT
void NativeContextToPalContext(const void* context, PAL_LIMITED_CONTEXT* palContext);
// Redirect Unix native context to the PAL_LIMITED_CONTEXT and also set the first two argument registers
void RedirectNativeContext(void* context, const PAL_LIMITED_CONTEXT* palContext, uintptr_t arg0Reg, uintptr_t arg1Reg);

#ifdef HOST_AMD64
// Get value of a register from the native context. The index is the processor specific
// register index stored in machine instructions.
uint64_t GetRegisterValueByIndex(void* context, uint32_t index);
// Get value of the program counter from the native context
uint64_t GetPC(void* context);
#endif // HOST_AMD64

struct NATIVE_CONTEXT
{
    ucontext_t ctx;

#ifdef TARGET_ARM64

    uint64_t& X0();
    uint64_t& X1();
    uint64_t& X2();
    uint64_t& X3();
    uint64_t& X4();
    uint64_t& X5();
    uint64_t& X6();
    uint64_t& X7();
    uint64_t& X8();
    uint64_t& X9();
    uint64_t& X10();
    uint64_t& X11();
    uint64_t& X12();
    uint64_t& X13();
    uint64_t& X14();
    uint64_t& X15();
    uint64_t& X16();
    uint64_t& X17();
    uint64_t& X18();
    uint64_t& X19();
    uint64_t& X20();
    uint64_t& X21();
    uint64_t& X22();
    uint64_t& X23();
    uint64_t& X24();
    uint64_t& X25();
    uint64_t& X26();
    uint64_t& X27();
    uint64_t& X28();
    uint64_t& Fp(); // X29
    uint64_t& Lr(); // X30
    uint64_t& Sp();
    uint64_t& Pc();

    uintptr_t GetIp() { return (uintptr_t)Pc(); }
    uintptr_t GetSp() { return (uintptr_t)Sp(); }

    template <typename F>
    void ForEachPossibleObjectRef(F lambda)
    {
        // it is doubtful anyone would implement X0-X28 not as a contiguous array
        // just in case - here are some asserts.
        ASSERT(&X0() + 1 == &X1());
        ASSERT(&X0() + 10 == &X10());
        ASSERT(&X0() + 20 == &X20());

        for (uint64_t* pReg = &X0(); pReg <= &X28(); pReg++)
            lambda((size_t*)pReg);

        // Lr can be used as a scratch register
        lambda((size_t*)&Lr());
    }

#elif defined(TARGET_AMD64)
    uint64_t& Rax();
    uint64_t& Rcx();
    uint64_t& Rdx();
    uint64_t& Rbx();
    uint64_t& Rsp();
    uint64_t& Rbp();
    uint64_t& Rsi();
    uint64_t& Rdi();
    uint64_t& R8 ();
    uint64_t& R9 ();
    uint64_t& R10();
    uint64_t& R11();
    uint64_t& R12();
    uint64_t& R13();
    uint64_t& R14();
    uint64_t& R15();
    uint64_t& Rip();

    uintptr_t GetIp() { return (uintptr_t)Rip(); }
    uintptr_t GetSp() { return (uintptr_t)Rsp(); }

    template <typename F>
    void ForEachPossibleObjectRef(F lambda)
    {
        lambda((size_t*)&Rax());
        lambda((size_t*)&Rcx());
        lambda((size_t*)&Rdx());
        lambda((size_t*)&Rbx());
        lambda((size_t*)&Rsp());
        lambda((size_t*)&Rbp());
        lambda((size_t*)&Rsi());
        lambda((size_t*)&Rdi());
        lambda((size_t*)&R8());
        lambda((size_t*)&R9());
        lambda((size_t*)&R10());
        lambda((size_t*)&R11());
        lambda((size_t*)&R12());
        lambda((size_t*)&R13());
        lambda((size_t*)&R14());
        lambda((size_t*)&R15());
    }
#elif defined(TARGET_ARM)
    uint64_t& Pc();
    uint64_t& Sp();
    uint64_t& Lr();
    uint64_t& R0();
    uint64_t& R1();
    uint64_t& R2();
    uint64_t& R3();
    uint64_t& R4();
    uint64_t& R5();
    uint64_t& R6();
    uint64_t& R7();
    uint64_t& R8();
    uint64_t& R9();
    uint64_t& R10();
    uint64_t& R11();
    uint64_t& R12();

    uintptr_t GetIp() { return (uintptr_t)Pc(); }
    uintptr_t GetSp() { return (uintptr_t)Sp(); }

    template <typename F>
    void ForEachPossibleObjectRef(F lambda)
    {
        lambda((size_t*)&R0());
        lambda((size_t*)&R1());
        lambda((size_t*)&R2());
        lambda((size_t*)&R3());
        lambda((size_t*)&R4());
        lambda((size_t*)&R5());
        lambda((size_t*)&R6());
        lambda((size_t*)&R7());
        lambda((size_t*)&R8());
        lambda((size_t*)&R9());
        lambda((size_t*)&R10());
        lambda((size_t*)&R11());
        lambda((size_t*)&R12());
    }

#elif defined(TARGET_LOONGARCH64)

    uint64_t& R0();
    uint64_t& Ra();
    uint64_t& R2();
    uint64_t& Sp();
    uint64_t& R4();
    uint64_t& R5();
    uint64_t& R6();
    uint64_t& R7();
    uint64_t& R8();
    uint64_t& R9();
    uint64_t& R10();
    uint64_t& R11();
    uint64_t& R12();
    uint64_t& R13();
    uint64_t& R14();
    uint64_t& R15();
    uint64_t& R16();
    uint64_t& R17();
    uint64_t& R18();
    uint64_t& R19();
    uint64_t& R20();
    uint64_t& R21();
    uint64_t& Fp();
    uint64_t& R23();
    uint64_t& R24();
    uint64_t& R25();
    uint64_t& R26();
    uint64_t& R27();
    uint64_t& R28();
    uint64_t& R29();
    uint64_t& R30();
    uint64_t& R31();
    uint64_t& Pc();

    uintptr_t GetIp() { return (uintptr_t)Pc(); }
    uintptr_t GetSp() { return (uintptr_t)Sp(); }

    template <typename F>
    void ForEachPossibleObjectRef(F lambda)
    {
        // it is doubtful anyone would implement R0,R2,R4-R21,R23-R31 not as a contiguous array
        // just in case - here are some asserts.
        ASSERT(&R4() + 1 == &R5());
        ASSERT(&R4() + 10 == &R14());

        for (uint64_t* pReg = &Ra(); pReg <= &R31(); pReg++)
            lambda((size_t*)pReg);

        // Ra can be used as a scratch register
        lambda((size_t*)&Ra());
    }

#elif defined(TARGET_RISCV64)

    uint64_t& R0();
    uint64_t& Ra();
    uint64_t& Sp();
    uint64_t& Gp();
    uint64_t& Tp();
    uint64_t& T0();
    uint64_t& T1();
    uint64_t& T2();
    uint64_t& Fp();
    uint64_t& S1();
    uint64_t& A0();
    uint64_t& A1();
    uint64_t& A2();
    uint64_t& A3();
    uint64_t& A4();
    uint64_t& A5();
    uint64_t& A6();
    uint64_t& A7();
    uint64_t& S2();
    uint64_t& S3();
    uint64_t& S4();
    uint64_t& S5();
    uint64_t& S6();
    uint64_t& S7();
    uint64_t& S8();
    uint64_t& S9();
    uint64_t& S10();
    uint64_t& S11();
    uint64_t& T3();
    uint64_t& T4();
    uint64_t& T5();
    uint64_t& T6();
    uint64_t& Pc();

    uintptr_t GetIp() { return (uintptr_t)Pc(); }
    uintptr_t GetSp() { return (uintptr_t)Sp(); }

    template <typename F>
    void ForEachPossibleObjectRef(F lambda)
    {
        // It is expected that registers are stored in a contiguous manner
        // Here are some asserts to check
        ASSERT(&A0() + 1 == &A1());
        ASSERT(&A0() + 7 == &A7());

        for (uint64_t* pReg = &Ra(); pReg <= &T6(); pReg++)
            lambda((size_t*)pReg);

        // Ra and Fp can be used as scratch registers
        lambda((size_t*)&Ra());
        lambda((size_t*)&Fp());
    }

#else
    PORTABILITY_ASSERT("NATIVE_CONTEXT");
#endif // TARGET_ARM
};

#endif // __NATIVE_CONTEXT_H__
