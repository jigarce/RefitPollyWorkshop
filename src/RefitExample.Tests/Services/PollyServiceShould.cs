﻿using FluentAssertions;
using Moq;
using RefitExample.Interfaces;
using RefitExample.Models;
using RefitExample.Services;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace RefitExample.Tests.Services
{
    public class PollyServiceShould
    {
        private readonly ILogger _logger;
        private readonly PollyService _pollyService;

        public PollyServiceShould(ITestOutputHelper output)
        {
            _logger = new TestLogger(output);
            _pollyService = new PollyService(_logger);
        }

        [Fact]
        public void Construct() =>
            _pollyService.Should().BeOfType<PollyService>();

        [Theory]
        [InlineData(PolicyTypes.CircuitBreakerWithFallBack)]
        [InlineData(PolicyTypes.CircuitBreakerWithRetryAndFallBack)]
        [InlineData(PolicyTypes.Fallback)]
        [InlineData(PolicyTypes.RetryWithFallBack)]
        public void ThrowWhenFallbackIsNull(PolicyTypes type) =>
            new Func<Task>(async () => await _pollyService.GetWithPolicy<string>(type, () => default(Task<string>), null))
            .Should().Throw<ArgumentNullException>();

        [Fact]
        public void RetryThreeTimesBeforeThrowing()
        {
            var mockLogger = new Mock<ILogger>();
            mockLogger.Setup(x => x.Write(It.Is<string>(y => y.Equals("RetryPolicy invoked")))).Verifiable();
            var pollyService = new PollyService(mockLogger.Object);

            var action = new Func<Task>(async () =>
                await pollyService.GetWithPolicy<string>(PolicyTypes.Retry,
                    () => throw new Exception("BOEM"),
                    null
                ));

            action.Should().Throw<Exception>().WithMessage("BOEM");

            mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("RetryPolicy invoked"))), Times.Exactly(3));
        }

        [Fact]
        public async Task ReturnFallbackData()
        {
            var mockLogger = new Mock<ILogger>();
            mockLogger.Setup(x => x.Write(It.Is<string>(y => y.Equals("FallbackPolicy invoked")))).Verifiable();
            var pollyService = new PollyService(mockLogger.Object);

            var result = await pollyService.GetWithPolicy<string>(PolicyTypes.Fallback,
                    () => throw new Exception("BOEM"),
                    () => Task.FromResult("test")
                );

            result.Should().Be("test");

            mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("FallbackPolicy invoked"))), Times.Exactly(1));
        }

        [Fact]
        public async Task RetryThreeTimesThenReturnFallback()
        {
            var mockLogger = new Mock<ILogger>();
            mockLogger.Setup(x => x.Write(It.Is<string>(y => y.Equals("FallbackPolicy invoked")))).Verifiable();
            var pollyService = new PollyService(mockLogger.Object);

            var result = await pollyService.GetWithPolicy<string>(PolicyTypes.RetryWithFallBack,
                    () => throw new Exception("BOEM"),
                    () => Task.FromResult("test")
                );

            result.Should().Be("test");

            mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("RetryPolicy invoked"))), Times.Exactly(3));
            mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("FallbackPolicy invoked"))), Times.Exactly(1));
        }

        [Fact]
        public async Task BreakCircuitAndThrow()
        {
            var mockLogger = new Mock<ILogger>();
            mockLogger.Setup(x => x.Write(It.Is<string>(y => y.Equals("Breaking circuit")))).Verifiable();

            var pollyService = new PollyService(mockLogger.Object, 10);

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var posts = await pollyService.GetWithPolicy<string>(
                        PolicyTypes.CircuitBreaker,
                        async () =>
                        {
                            await Task.Delay(40);
                            throw new Exception("BOEM");
                        },
                        null).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // expected
                }
            }

            mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("Breaking circuit"))), Times.AtLeast(1));
        }

        [Fact]
        public async Task RetryThreeTimesAndReturnFallbackData()
        {
            var mockLogger = new Mock<ILogger>();
            mockLogger.Setup(x => x.Write(It.Is<string>(y => y.Equals("RetryPolicy invoked")))).Verifiable();

            var pollyService = new PollyService(mockLogger.Object);

            var data = await pollyService.GetWithPolicy<string>(
                                PolicyTypes.RetryWithFallBack,
                                () => throw new Exception("BOEM"),
                                () => Task.FromResult("test")).ConfigureAwait(false);

            data.Should().Be("test");

            mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("RetryPolicy invoked"))), Times.Exactly(3));
        }

        [Fact]
        public async Task BreakCircuitReturnFallBackData()
        {
            var mockLogger = new Mock<ILogger>();
            mockLogger.Setup(x => x.Write(It.Is<string>(y => y.Equals("FallbackPolicy invoked")))).Verifiable();
            mockLogger.Setup(x => x.Write(It.Is<string>(y => y.Equals("Breaking circuit")))).Verifiable();

            var pollyService = new PollyService(mockLogger.Object);

            for (int i = 0; i < 10; i++)
            {
                var data = await pollyService.GetWithPolicy<string>(
                PolicyTypes.CircuitBreakerWithFallBack,
                () => throw new Exception("BOEM"),
                () => Task.FromResult("test")).ConfigureAwait(false);

                data.Should().Be("test");
                mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("FallbackPolicy invoked"))), Times.AtLeastOnce());
            }

            mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("Breaking circuit"))), Times.AtLeastOnce());
        }

        [Fact]
        public async Task BreakCircuitRetryAndFinallyReturnFallbackData()
        {
            var mockLogger = new Mock<ILogger>();
            mockLogger.Setup(x => x.Write(It.Is<string>(y => y.Equals("FallbackPolicy invoked")))).Verifiable();
            mockLogger.Setup(x => x.Write(It.Is<string>(y => y.Equals("Breaking circuit")))).Verifiable();
            mockLogger.Setup(x => x.Write(It.Is<string>(y => y.Equals("RetryPolicy invoked")))).Verifiable();

            var pollyService = new PollyService(mockLogger.Object);

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var data = await pollyService.GetWithPolicy<string>(
                        PolicyTypes.CircuitBreakerWithRetryAndFallBack,
                        () => throw new Exception("BOEM"),
                        () => Task.FromResult("test")).ConfigureAwait(false);
                    data.Should().Be("test");
                    mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("FallbackPolicy invoked"))), Times.AtLeastOnce());
                    mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("RetryPolicy invoked"))), Times.Exactly(3));
                }
                catch (Exception ex)
                {
                    // expected
                }
            }
            mockLogger.Verify(x => x.Write(It.Is<string>(y => y.Equals("Breaking circuit"))), Times.AtLeastOnce());
        }

        [Fact]
        public async Task ExecuteWithoutPolicies()
        {
            var result = await _pollyService.GetWithPolicy<string>(
                PolicyTypes.None,
                () => Task.FromResult("test"),
                null);
            result.Should().Be("test");
        }
    }
}